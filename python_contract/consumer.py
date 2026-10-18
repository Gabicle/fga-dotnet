"""Fail-closed authorization check client; no AI or application-domain dependencies."""

from __future__ import annotations

import base64
import binascii
import http.client
import ipaddress
import json
import math
import os
import re
import socket
import ssl
import time
from dataclasses import dataclass
from typing import Callable, Mapping, TypeVar
from urllib.parse import urlsplit


DECISION_ID = re.compile(r"[0-9a-f]{32}\Z")
MAX_RESPONSE_BYTES = 65_536
T = TypeVar("T")


class ConfigurationError(ValueError):
    """Required trusted service configuration is absent or invalid."""


@dataclass(frozen=True)
class SubjectAttributes:
    region: str
    clearance_level: int


@dataclass(frozen=True)
class ResourceAttributes:
    region: str
    sensitivity_level: int
    status: str | None = None


@dataclass(frozen=True)
class AuthorizationRequest:
    # Trusted application code selects all of these fields. Tool arguments are separate.
    subject_type: str
    subject_id: str
    object_type: str
    object_id: str
    relation: str
    subject_attributes: SubjectAttributes | None = None
    resource_attributes: ResourceAttributes | None = None

    def payload(self) -> dict[str, object]:
        body: dict[str, object] = {
            "subjectType": self.subject_type,
            "subjectId": self.subject_id,
            "objectType": self.object_type,
            "objectId": self.object_id,
            "relation": self.relation,
        }
        if self.subject_attributes is not None:
            body["subjectAttributes"] = {
                "subjectType": self.subject_type,
                "subjectId": self.subject_id,
                "region": self.subject_attributes.region,
                "clearanceLevel": self.subject_attributes.clearance_level,
            }
        if self.resource_attributes is not None:
            attributes: dict[str, object] = {
                "resourceType": self.object_type,
                "resourceId": self.object_id,
                "region": self.resource_attributes.region,
                "sensitivityLevel": self.resource_attributes.sensitivity_level,
            }
            if self.resource_attributes.status is not None:
                attributes["status"] = self.resource_attributes.status
            body["resourceAttributes"] = attributes
        return body


@dataclass(frozen=True)
class AuthorizationDecision:
    approved: bool
    decision_id: str | None = None
    reason_code: str | None = None
    failure: str | None = None


@dataclass(frozen=True)
class OperationResult:
    executed: bool
    value: object | None
    decision_id: str | None
    reason_code: str | None
    failure: str | None


class CheckClient:
    """One check per call. It does not follow redirects or retry approvals."""

    def __init__(
        self,
        service_url: str,
        check_key: str,
        *,
        connect_timeout: float = 2.0,
        read_timeout: float = 2.0,
        request_timeout: float = 5.0,
    ) -> None:
        if not isinstance(check_key, str) or not check_key:
            raise ConfigurationError("FGA_CHECK_API_KEY is required.")
        try:
            decoded = base64.b64decode(check_key, validate=True)
        except (ValueError, binascii.Error):
            raise ConfigurationError("FGA_CHECK_API_KEY must encode 32 bytes.") from None
        if len(decoded) != 32 or len(set(decoded)) == 1:
            raise ConfigurationError("FGA_CHECK_API_KEY must encode a non-degenerate 32-byte key.")

        try:
            parsed = urlsplit(service_url)
        except (TypeError, ValueError):
            raise ConfigurationError("FGA_AUTHORIZATION_URL must be a service origin.") from None
        if (
            parsed.scheme not in ("http", "https")
            or not parsed.hostname
            or parsed.username is not None
            or parsed.password is not None
            or parsed.path not in ("", "/")
            or parsed.query
            or parsed.fragment
        ):
            raise ConfigurationError("FGA_AUTHORIZATION_URL must be a service origin.")
        try:
            port = parsed.port
        except ValueError:
            raise ConfigurationError("FGA_AUTHORIZATION_URL has an invalid port.") from None
        if port is not None and port < 1:
            raise ConfigurationError("FGA_AUTHORIZATION_URL has an invalid port.")
        if parsed.scheme == "http" and not _is_loopback(parsed.hostname):
            raise ConfigurationError("Plain HTTP is allowed only for a loopback service URL.")
        for value in (connect_timeout, read_timeout, request_timeout):
            if not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
                raise ConfigurationError("Authorization timeouts must be positive finite seconds.")

        self._scheme = parsed.scheme
        self._host = parsed.hostname
        self._port = port
        self._key = check_key
        self._connect_timeout = connect_timeout
        self._read_timeout = read_timeout
        self._request_timeout = request_timeout

    @classmethod
    def from_environment(cls, environ: Mapping[str, str] | None = None) -> CheckClient:
        source = os.environ if environ is None else environ
        url = source.get("FGA_AUTHORIZATION_URL")
        key = source.get("FGA_CHECK_API_KEY")
        if not url or not key:
            raise ConfigurationError("FGA_AUTHORIZATION_URL and FGA_CHECK_API_KEY are required.")
        return cls(url, key)

    def check(self, request: AuthorizationRequest) -> AuthorizationDecision:
        conn: http.client.HTTPConnection | None = None
        deadline = time.monotonic() + self._request_timeout
        try:
            connection_type = (
                http.client.HTTPSConnection if self._scheme == "https" else http.client.HTTPConnection
            )
            conn = connection_type(
                self._host, self._port,
                timeout=min(self._connect_timeout, _remaining(deadline)),
            )
            conn.connect()
            sock = conn.sock
            if sock is None:
                return AuthorizationDecision(False, failure="transport_error")
            body = json.dumps(request.payload(), separators=(",", ":")).encode("utf-8")
            _set_read_timeout(sock, self._read_timeout, deadline)
            conn.request(
                "POST", "/api/permissions/check", body=body,
                headers={
                    "Authorization": "ApiKey " + self._key,
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                },
            )
            _set_read_timeout(sock, self._read_timeout, deadline)
            response = conn.getresponse()
            decision_id = _decision_id(response)
            if response.status != 200:
                return AuthorizationDecision(False, decision_id, failure=f"http_{response.status}")
            if response.getheader("Content-Type", "").split(";", 1)[0].strip().lower() != "application/json":
                return AuthorizationDecision(False, decision_id, failure="invalid_response")

            chunks: list[bytes] = []
            size = 0
            while True:
                _set_read_timeout(sock, self._read_timeout, deadline)
                chunk = response.read1(min(4096, MAX_RESPONSE_BYTES + 1 - size))
                if not chunk:
                    break
                size += len(chunk)
                if size > MAX_RESPONSE_BYTES:
                    return AuthorizationDecision(False, decision_id, failure="invalid_response")
                chunks.append(chunk)
            if response.length not in (None, 0):
                return AuthorizationDecision(False, decision_id, failure="invalid_response")
            try:
                data = json.loads(b"".join(chunks).decode("utf-8"))
            except (ValueError, UnicodeError):
                return AuthorizationDecision(False, decision_id, failure="invalid_response")
            return _validate_response(data, decision_id)
        except Exception:
            # Transport, protocol, and unexpected client failures all fail closed.
            return AuthorizationDecision(False, failure="transport_error")
        finally:
            if conn is not None:
                try:
                    conn.close()
                except Exception:
                    pass


def execute_if_authorized(
    client: CheckClient,
    request: AuthorizationRequest,
    tool_arguments: Mapping[str, object],
    operation: Callable[[Mapping[str, object]], T],
) -> OperationResult:
    """Only trusted `request` selects identity and policy; arguments reach the tool only."""
    decision = client.check(request)
    if not decision.approved:
        return OperationResult(False, None, decision.decision_id, decision.reason_code, decision.failure)
    return OperationResult(True, operation(tool_arguments), decision.decision_id, decision.reason_code, None)


def _is_loopback(host: str) -> bool:
    if host == "localhost":
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


def _remaining(deadline: float) -> float:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError("Authorization request timed out.")
    return remaining


def _set_read_timeout(sock: socket.socket, read_timeout: float, deadline: float) -> None:
    sock.settimeout(min(read_timeout, _remaining(deadline)))


def _decision_id(response: http.client.HTTPResponse) -> str | None:
    values = response.headers.get_all("X-Authorization-Decision-Id", [])
    return values[0] if len(values) == 1 and DECISION_ID.fullmatch(values[0]) else None


def _validate_response(data: object, decision_id: str | None) -> AuthorizationDecision:
    if not isinstance(data, dict) or decision_id is None:
        return AuthorizationDecision(False, decision_id, failure="invalid_response")
    allowed = data.get("allowed")
    reason = data.get("reason")
    code = data.get("reasonCode")
    if type(allowed) is not bool or not isinstance(reason, str) or not reason.strip():
        return AuthorizationDecision(False, decision_id, failure="invalid_response")
    if allowed is True and code == "authorization_allowed":
        return AuthorizationDecision(True, decision_id, code)
    if allowed is False and code in ("relationship_denied", "required_abac_denied"):
        return AuthorizationDecision(False, decision_id, code)
    return AuthorizationDecision(False, decision_id, failure="invalid_response")
