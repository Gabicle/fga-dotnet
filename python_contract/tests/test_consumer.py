from __future__ import annotations

import base64
import http.server
import json
import os
import socket
import threading
import unittest
from unittest import mock

from python_contract.consumer import (
    AuthorizationRequest,
    CheckClient,
    ConfigurationError,
    ResourceAttributes,
    SubjectAttributes,
    execute_if_authorized,
)


DECISION_ID = "0123456789abcdef0123456789abcdef"
ALLOW = {"allowed": True, "reason": "Allowed via tuple expansion", "reasonCode": "authorization_allowed"}
DENY = {"allowed": False, "reason": "No matching tuple found", "reasonCode": "relationship_denied"}


class Handler(http.server.BaseHTTPRequestHandler):
    def do_POST(self) -> None:
        body = self.rfile.read(int(self.headers["Content-Length"]))
        self.server.seen.append((self.path, dict(self.headers), json.loads(body)))
        self.server.request_seen.set()
        if self.server.block_response:
            self.server.release.wait(5)
        self.send_response(self.server.status)
        for name, value in self.server.response_headers.items():
            self.send_header(name, value)
        self.end_headers()
        try:
            self.wfile.write(self.server.body)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, _format: str, *args: object) -> None:
        pass


class ConsumerContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.key = base64.b64encode(os.urandom(32)).decode("ascii")
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.server.daemon_threads = True
        self.server.seen = []
        self.server.status = 200
        self.server.body = json.dumps(ALLOW).encode("utf-8")
        self.server.response_headers = {
            "Content-Type": "application/json",
            "X-Authorization-Decision-Id": DECISION_ID,
        }
        self.server.block_response = False
        self.server.request_seen = threading.Event()
        self.server.release = threading.Event()
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.client = CheckClient(
            f"http://127.0.0.1:{self.server.server_port}", self.key,
            connect_timeout=0.5, read_timeout=0.5, request_timeout=2.0,
        )
        self.request = AuthorizationRequest(
            "user", "trusted-user", "document", "trusted-document", "viewer"
        )
        self.executions = 0

    def tearDown(self) -> None:
        self.server.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)

    def operation(self, arguments: dict[str, object]) -> str:
        self.executions += 1
        return str(arguments["value"])

    def run_tool(self):
        return execute_if_authorized(
            self.client, self.request,
            {"value": "executed", "subjectId": "forged-user", "relation": "admin"},
            self.operation,
        )

    def test_explicit_allow_executes_once_and_tool_arguments_cannot_select_identity(self) -> None:
        result = self.run_tool()
        self.assertTrue(result.executed)
        self.assertEqual("executed", result.value)
        self.assertEqual(DECISION_ID, result.decision_id)
        self.assertEqual("authorization_allowed", result.reason_code)
        self.assertEqual(1, self.executions)
        self.assertEqual(1, len(self.server.seen))
        path, headers, payload = self.server.seen[0]
        self.assertEqual("/api/permissions/check", path)
        self.assertEqual("ApiKey " + self.key, headers["Authorization"])
        self.assertEqual("trusted-user", payload["subjectId"])
        self.assertEqual("trusted-document", payload["objectId"])
        self.assertEqual("viewer", payload["relation"])
        self.assertNotIn("forged-user", str(payload))

    def test_valid_denial_never_executes(self) -> None:
        self.server.body = json.dumps(DENY).encode()
        result = self.run_tool()
        self.assertFalse(result.executed)
        self.assertEqual("relationship_denied", result.reason_code)
        self.assertEqual(DECISION_ID, result.decision_id)
        self.assertEqual(0, self.executions)

    def test_invalid_200_responses_never_execute(self) -> None:
        cases = [
            ({**ALLOW, "allowed": "true"}, None),
            ({**ALLOW, "allowed": 1}, None),
            ({"reason": "x", "reasonCode": "authorization_allowed"}, None),
            ({**ALLOW, "reason": ""}, None),
            ({**ALLOW, "reasonCode": "relationship_denied"}, None),
            ({**DENY, "reasonCode": "authorization_allowed"}, None),
            ({**ALLOW, "reasonCode": None}, None),
            ([ALLOW], None),
            (ALLOW, "missing_id"),
            (ALLOW, "invalid_id"),
            (ALLOW, "invalid_content_type"),
            (ALLOW, "truncated_length"),
            (None, "malformed_json"),
        ]
        for body, mutation in cases:
            with self.subTest(body=body, mutation=mutation):
                self.server.body = b"{" if mutation == "malformed_json" else json.dumps(body).encode()
                self.server.response_headers = {
                    "Content-Type": "application/json",
                    "X-Authorization-Decision-Id": DECISION_ID,
                }
                if mutation == "missing_id":
                    del self.server.response_headers["X-Authorization-Decision-Id"]
                elif mutation == "invalid_id":
                    self.server.response_headers["X-Authorization-Decision-Id"] = "invalid"
                elif mutation == "invalid_content_type":
                    self.server.response_headers["Content-Type"] = "text/plain"
                elif mutation == "truncated_length":
                    self.server.response_headers["Content-Length"] = "1000"
                result = self.run_tool()
                self.assertFalse(result.executed)
                self.assertEqual(0, self.executions)

    def test_all_error_statuses_and_redirects_never_execute_or_retry(self) -> None:
        for status in (400, 401, 403, 404, 409, 500, 302):
            with self.subTest(status=status):
                self.server.status = status
                self.server.response_headers["Location"] = "/api/permissions/check"
                before = len(self.server.seen)
                result = self.run_tool()
                self.assertFalse(result.executed)
                self.assertEqual(0, self.executions)
                self.assertEqual(before + 1, len(self.server.seen))
                self.assertEqual(DECISION_ID, result.decision_id)

    def test_connection_refusal_fails_closed(self) -> None:
        with socket.socket() as port_holder:
            port_holder.bind(("127.0.0.1", 0))
            # Keep the port reserved but not listening, so another process cannot race us.
            self.client = CheckClient(
                f"http://127.0.0.1:{port_holder.getsockname()[1]}", self.key,
                connect_timeout=0.2, read_timeout=0.2, request_timeout=0.5,
            )
            result = self.run_tool()
        self.assertFalse(result.executed)
        self.assertEqual("transport_error", result.failure)
        self.assertEqual(0, self.executions)

    def test_read_timeout_fails_closed(self) -> None:
        self.server.block_response = True
        self.client = CheckClient(
            f"http://127.0.0.1:{self.server.server_port}", self.key,
            connect_timeout=0.2, read_timeout=0.1, request_timeout=0.3,
        )
        result = self.run_tool()
        self.assertTrue(self.server.request_seen.wait(1))
        self.assertFalse(result.executed)
        self.assertEqual("transport_error", result.failure)
        self.assertEqual(0, self.executions)

    def test_unexpected_transport_exception_fails_closed(self) -> None:
        with mock.patch("python_contract.consumer.http.client.HTTPConnection",
                        side_effect=RuntimeError("transport failed")):
            result = self.run_tool()
        self.assertFalse(result.executed)
        self.assertEqual(0, self.executions)

    def test_required_config_and_trusted_attribute_payload(self) -> None:
        def protected_entrypoint(environment: dict[str, str]) -> None:
            configured_client = CheckClient.from_environment(environment)
            execute_if_authorized(configured_client, self.request,
                                  {"value": "must-not-run"}, self.operation)

        with self.assertRaises(ConfigurationError) as missing:
            protected_entrypoint({})
        self.assertNotIn(self.key, str(missing.exception))
        self.assertEqual(0, self.executions)
        with self.assertRaises(ConfigurationError):
            CheckClient("http://example.com", self.key)
        with self.assertRaises(ConfigurationError):
            CheckClient("http://127.0.0.1", "invalid")

        typed = AuthorizationRequest(
            "user", "trusted-user", "report", "trusted-report", "editor",
            SubjectAttributes("eu-west", 3), ResourceAttributes("eu-west", 2, "draft"),
        )
        payload = typed.payload()
        self.assertEqual("trusted-user", payload["subjectAttributes"]["subjectId"])
        self.assertEqual("trusted-report", payload["resourceAttributes"]["resourceId"])
        self.assertEqual("report", payload["resourceAttributes"]["resourceType"])


if __name__ == "__main__":
    unittest.main()
