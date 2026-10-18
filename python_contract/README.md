# Python authorization contract example

This dependency-free Python 3.12 example shows how a separate application can
consume `fga-dotnet` over HTTP. It is a contract example and simulated protected
operation, not an AI agent or a portfolio application.

`consumer.py` has immutable request and attribute types, a check-only HTTP client,
and `execute_if_authorized`. The wrapper receives trusted authorization context
separately from tool arguments. It calls the operation only after an explicit
HTTP 200 approval. Tool arguments cannot replace the acting user, canonical
object type, relation, resource ID, or ABAC attributes in that request.

Run the isolated contract tests without service credentials:

```bash
python3 -m unittest discover -s python_contract/tests -v
```

For a live client, the check key must match the API's
`Permissions__ApiKeys__Check` value. Generate the API keys independently with
`openssl rand -base64 32` as described in the main README. In deployment, use HTTPS and
store the key in a secret manager or protected environment, never in source or
model-visible tool arguments. Plain HTTP is accepted only for a loopback URL.
The example has no third-party Python packages. It uses explicit connection,
socket read, and overall request deadlines, follows no redirects, and retries no
authorization approval. DNS lookup time is outside Python's socket timeout;
resolve the service through trusted local infrastructure. The response body is
limited to 64 KiB.

`check()` approves only HTTP 200 JSON objects with a valid decision ID,
nonempty string `reason`, exact Boolean `allowed: true`, and
`reasonCode: authorization_allowed`. Boolean false with a documented denial code
is a denial. Every other status, malformed response, missing metadata, timeout,
or connection failure is non-authorization. The caller can retain the decision
ID for correlation; it must not treat a decision ID alone as approval.

## Live disposable proof

Use a PostgreSQL 16 **disposable** database and run the API with separate,
independently generated check and admin keys. Apply EF Core migrations as shown
in the repository README. The proof script creates its own simulated document
and report tuples using the admin key; the consumer itself uses only the check
key. Do not provide the admin key to a real checking application.

```bash
export FGA_AUTHORIZATION_URL=http://127.0.0.1:5031
export FGA_CHECK_API_KEY="$Permissions__ApiKeys__Check"
export FGA_ADMIN_API_KEY="$Permissions__ApiKeys__Admin" # proof setup only
python3 -m python_contract.live_proof
```

The script prints only decision IDs, the admin-forbidden status, and the
simulated operation count. Match each decision ID to an `AuthorizationDecision`
event in the API's structured logs. Stop the API, then run:

```bash
python3 -m python_contract.live_proof outage
```

When the API runs in a Docker host-network namespace that host Python cannot
reach, run the same commands in an official Python 3.12 container sharing that
network. Put the three `FGA_` settings above in a local, permission-restricted
`/tmp/fga-proof.env` file; never commit it:

```bash
docker run --rm --network host --env-file /tmp/fga-proof.env \
  --mount type=bind,src="$PWD",dst=/src,readonly -w /src \
  python:3.12-slim python -m python_contract.live_proof
# After stopping the API, repeat with "outage" appended.
```

The outage mode requires the service URL to be unavailable and verifies the
protected operation is not invoked. Neither mode prints credentials or raw
security attributes. Keep `Information` events from
`Permissions.Api.Controllers.PermissionsController` in a restricted log sink
with a defined retention period. The independent Python application must
establish the end-user identity and security attributes from trusted data; a
valid service key does not prove that caller-supplied claims are authentic.
