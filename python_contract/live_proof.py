"""Disposable-data smoke proof against a running fga-dotnet API.

The administrative key is used here only to create test tuples. A real checking
service receives only FGA_CHECK_API_KEY.
"""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request
import uuid

from python_contract.consumer import AuthorizationRequest, CheckClient, execute_if_authorized


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def _require(condition: bool, check: str) -> None:
    if not condition:
        raise RuntimeError("Live authorization proof failed: " + check)


def _post(path: str, key: str, body: dict[str, object]) -> int:
    url = os.environ["FGA_AUTHORIZATION_URL"].rstrip("/") + path
    request = urllib.request.Request(
        url,
        data=json.dumps(body).encode("utf-8"),
        headers={"Authorization": "ApiKey " + key, "Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.build_opener(
            urllib.request.ProxyHandler({}), _NoRedirect
        ).open(request, timeout=5) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def main() -> None:
    client = CheckClient.from_environment()
    executions = 0

    def simulated_operation(_arguments: dict[str, object]) -> str:
        nonlocal executions
        executions += 1
        return "simulated-operation-executed"

    if len(sys.argv) == 2 and sys.argv[1] == "outage":
        result = execute_if_authorized(
            client, AuthorizationRequest("user", "proof-user", "document", "unavailable", "viewer"),
            {"objectId": "model-controlled-value"}, simulated_operation,
        )
        _require(not result.executed and executions == 0 and result.failure == "transport_error",
                 "service outage must deny execution")
        print(json.dumps({"outage": "failed_closed", "executions": executions}))
        return
    if len(sys.argv) != 1:
        raise SystemExit("Usage: python -m python_contract.live_proof [outage]")

    admin_key = os.environ.get("FGA_ADMIN_API_KEY")
    if not admin_key:
        raise SystemExit("FGA_ADMIN_API_KEY is required only for disposable proof setup.")
    suffix = uuid.uuid4().hex[:12]
    subject_id = "proof-user-" + suffix
    document_id = "proof-document-" + suffix
    other_document_id = "proof-other-document-" + suffix
    report_id = "proof-report-" + suffix

    for object_type, object_id in (("document", document_id), ("report", report_id)):
        grant = {
            "subjectType": "user", "subjectId": subject_id, "relation": "viewer",
            "objectType": object_type, "objectId": object_id,
        }
        _require(_post("/api/permissions/grant", admin_key, grant) == 204,
                 "administrative test grant")

    # The check-only key cannot establish another relationship.
    _require(_post("/api/permissions/grant", os.environ["FGA_CHECK_API_KEY"], {
        "subjectType": "user", "subjectId": subject_id, "relation": "viewer",
        "objectType": "document", "objectId": other_document_id,
    }) == 403, "check-only key must be forbidden from granting")

    allowed = execute_if_authorized(
        client, AuthorizationRequest("user", subject_id, "document", document_id, "viewer"),
        {"subjectId": "model-forgery", "relation": "editor"}, simulated_operation,
    )
    _require(allowed.executed and executions == 1 and
             allowed.reason_code == "authorization_allowed" and allowed.decision_id is not None,
             "explicit allow must execute once with a decision ID")

    denied = execute_if_authorized(
        client, AuthorizationRequest("user", subject_id, "document", other_document_id, "viewer"),
        {"objectId": document_id}, simulated_operation,
    )
    _require(not denied.executed and executions == 1 and
             denied.reason_code == "relationship_denied" and denied.decision_id is not None,
             "cross-resource denial must not execute")

    invalid_abac = execute_if_authorized(
        client, AuthorizationRequest("user", subject_id, "report", report_id, "viewer"),
        {"subjectAttributes": {"region": "model-forgery"}}, simulated_operation,
    )
    _require(not invalid_abac.executed and executions == 1 and
             invalid_abac.failure == "http_400" and invalid_abac.decision_id is not None,
             "missing required ABAC context must not execute")

    # The IDs are safe to print and can be matched to AuthorizationDecision logs.
    print(json.dumps({
        "allowed_decision_id": allowed.decision_id,
        "denied_decision_id": denied.decision_id,
        "invalid_context_decision_id": invalid_abac.decision_id,
        "check_only_admin_status": 403,
        "executions": executions,
    }))


if __name__ == "__main__":
    main()
