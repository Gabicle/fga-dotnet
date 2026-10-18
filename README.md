# Permissions - Fine-Grained Access Control

A Zanzibar-inspired fine-grained access control system built in C# with PostgreSQL. Implements relation tuple-based permissions with recursive expansion, role inheritance, and attribute-based policy evaluation (ABAC).

Built as a reference implementation for financial planning platforms where a CFO sees everything, a regional manager sees only their region, and an analyst can edit only their own rows.

---

## What this is

Most permission systems are binary. A user has a role, a role has permissions, and that is it. This breaks down the moment you need:

- A user to have different access levels on different resources
- Group membership to propagate permissions transitively
- Time-bounded access that expires automatically
- Context-aware rules that deny access if the resource is locked, or if the user is in the wrong region

This system solves all of that with one unified model: **relation tuples**.

---

## The model - relation tuples

Every permission in the system is one row:

```
object_type : object_id # relation @ subject_type : subject_id
```

Examples:

```
report:42#viewer@user:7              → user 7 can view report 42
role:editor#member@user:7            → user 7 is a member of the editor role
report:42#viewer@role:editor#member  → editor role members can view report 42
```

The last example uses `SubjectRelation` where the subject is not a specific user but anyone who satisfies a relation on another object. This is how group membership and role-based access work without a separate table.

### Field mapping

```
RelationTuple.Create("report", "42", "viewer", "user", "7")

Property       Value       Meaning
────────────   ────────    ─────────────────────────────────
ObjectType  =  "report"    Type of thing being protected
ObjectId    =  "42"        Which specific report
Relation    =  "viewer"    Type of access
SubjectType =  "user"      Type of thing being granted access
SubjectId   =  "7"         Which specific user
```

---

## Architecture

```
Permissions.Api             → HTTP controllers, OpenAPI
Permissions.Application     → Check engine, permission service, ABAC evaluator
Permissions.Domain          → Entities, value objects, repository interfaces
Permissions.Infrastructure  → EF Core, PostgreSQL repositories
```

Dependency direction: `Api → Application → Domain ← Infrastructure`

Domain has zero knowledge of EF Core, PostgreSQL, or HTTP. Infrastructure implements domain interfaces. Application orchestrates domain logic. API handles transport.

### The check algorithm

A permission check runs in two layers:

**Layer 1 - Zanzibar tuple expansion (recursive)**

```
Check: can user:7 view report:42?

Step 1: Direct tuple - does report:42#viewer@user:7 exist?
        YES → allowed immediately

Step 2: Indirect expansion - does report:42#viewer@{something}#{relation} exist?
        Found: report:42#viewer@role:editor#member
        Recursively: can user:7 have relation "member" on role:editor?
        → Direct tuple role:editor#member@user:7? YES → allowed

Step 3: Role membership inheritance - if checking role:viewer#member,
        inspect descendant roles. An editor member is also a viewer member
        when editor's parent is viewer; the reverse does not apply.
```

A `HashSet<string>` tracks visited expansion states. Implicit role inheritance is
limited to `member`; malformed cyclic role graphs and graphs exceeding 1,024
roles deny implicit inheritance for that check. Tuple expansion stops after
4,096 states or 128 recursive steps. Direct resource tuples are checked before
inheritance. Repository failures propagate as errors; callers must treat an
unavailable or failed check as denied access, never as an allow decision.

**Layer 2 - ABAC policy evaluation**

If tuple expansion allows, server-owned requirements determine whether ABAC runs as a
second gate. Required policies must all pass:

```
RegionMatchPolicy     - subject.region must match resource.region
ResourceStatusPolicy  - deny if resource.status is "locked" or "archived"
SensitivityLevelPolicy - subject.clearanceLevel must be >= resource.sensitivityLevel
```

The current required operations are:

| Checked operation | Required policies | Required fields |
| --- | --- | --- |
| `report/viewer` | RegionMatchPolicy, SensitivityLevelPolicy | Subject and resource identities, regions, subject clearance level, resource sensitivity level |
| `report/editor` | RegionMatchPolicy, ResourceStatusPolicy, SensitivityLevelPolicy | The same fields, plus resource status |

For `report/editor`, supported status values are `draft`, `locked`, and `archived`.
`locked` and `archived` deny editing; an absent or unsupported status is a malformed
request. The server refuses to start if a required policy is not registered.
Requirements are selected from the top-level checked `objectType` and `relation`;
nested attribute identities must agree with the checked subject and object. An
operation without a registered requirement is **ReBAC-only**, even when attributes
are supplied. New sensitive operations must be explicitly added to the server-owned
requirement map before relying on ABAC. Policy names and status values are generic
examples, not a complete resource lifecycle schema.

The authenticated checking client must choose the canonical `objectType` and
`relation` for the operation it will actually perform. Trusted application code
must make that choice; model-generated content and arbitrary user input must not
select or override it. These names are exact-match identifiers: case variants
and aliases are distinct authorization relationships and cannot reuse a canonical
tuple grant. A separately granted variant is an unlisted, ReBAC-only operation
unless it has its own ABAC requirement. Consumers must explicitly register every
sensitive operation that needs ABAC. This service cannot independently verify
that the caller chose the correct business operation.

---

## Database schema

```sql
relation_tuples
  id, object_type, object_id, relation,
  subject_type, subject_id, subject_relation,
  expires_at, created_at

roles
  id, name, normalized_name, description,
  parent_role_id, created_at
```

Key indexes:

```sql
-- Fast permission lookups
UNIQUE INDEX ix_relation_tuples_identity
  ON relation_tuples (object_type, object_id, relation, subject_type, subject_id, subject_relation)
  NULLS NOT DISTINCT

-- Subject-based queries ("what can this user access?")
INDEX ix_relation_tuples_subject
  ON relation_tuples (subject_type, subject_id)

-- Expiry cleanup
INDEX ix_relation_tuples_expires_at
  ON relation_tuples (expires_at)
```

---

## Running locally

**Prerequisites:** .NET SDK 10, Docker, and PostgreSQL 16 (the commands below run PostgreSQL in containers). From a clean checkout, use the official NuGet.org source in `nuget.config`:

```bash
dotnet restore Permissions.slnx --force
dotnet build Permissions.slnx --configuration Release --no-restore
dotnet tool install --global dotnet-ef --version 10.0.5
```

Make sure your global .NET tools directory (`~/.dotnet/tools`) is on `PATH` before running `dotnet ef`. If `dotnet-ef` is already installed, update it to the same version instead of installing it again.

```bash
# Use two distinct, randomly generated 256-bit service credentials.
export Permissions__ApiKeys__Check="$(openssl rand -base64 32)"
export Permissions__ApiKeys__Admin="$(openssl rand -base64 32)"

# Start the application database. The host port matches appsettings.json.
docker run --name permissions-db \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_DB=permissions \
  -p 5437:5432 -d postgres:16

docker exec permissions-db pg_isready -U postgres -d permissions

# Apply migrations
dotnet ef database update \
  --project Permissions.Infrastructure \
  --startup-project Permissions.Api

# Run the API
dotnet run --project Permissions.Api --launch-profile http
```

API available at `http://localhost:5031`. OpenAPI schema at `http://localhost:5031/openapi/v1.json`.

The API refuses to start unless both API keys are configured as distinct Base64-encoded 32-byte values; it also rejects all-identical-byte keys. Key strength cannot be proven from supplied bytes. Generate each key independently with a cryptographically secure random generator, such as the `openssl rand` commands above. Supply keys through environment variables or a secure configuration provider; do not put deployed keys in appsettings files or source control. Use HTTPS for network traffic. Rotate a key by changing its server configuration and restarting the API, then update the corresponding client. The development OpenAPI endpoint also requires a valid service key.

The committed connection string is a local development default only. For a different database, set `ConnectionStrings__DefaultConnection` in the API environment; keep deployed database credentials outside source control.

### Run the tests from a clean checkout

The PostgreSQL suite **migrates and deletes its target database**. Use a separate PostgreSQL 16 container and a database named `permissions_integration_disposable` (or a name matching the documented disposable convention), never the application database. The test-only authorization setting must be the exact value below. The fixture rejects missing or non-disposable targets before connecting or cleaning up.

```bash
docker run --name permissions-test-db \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_DB=permissions_integration_disposable \
  -p 5438:5432 -d postgres:16
docker exec permissions-test-db pg_isready -U postgres -d permissions_integration_disposable

export INTEGRATION_TEST_CONNECTION_STRING="Host=localhost;Port=5438;Database=permissions_integration_disposable;Username=postgres;Password=postgres"
export PERMISSIONS_TEST_ALLOW_DESTRUCTIVE_DATABASE=disposable-tests-only

dotnet test Permissions.Domain.Tests/Permissions.Domain.Tests.csproj --no-build --configuration Release
dotnet test Permissions.Application.Tests/Permissions.Application.Tests.csproj --no-build --configuration Release
dotnet test Permissions.Api.Tests/Permissions.Api.Tests.csproj --no-build --configuration Release
dotnet test Permissions.Integration.Tests/Permissions.Integration.Tests.csproj --no-build --configuration Release
dotnet list Permissions.slnx package --vulnerable --include-transitive
```

The fixture accepts only explicit targets named `permissions_integration_disposable` or `permissions_integration_disposable_<lowercase letters or digits>`; the migration test creates similarly protected `permissions_tuple_migration_disposable_<id>` databases. It rejects `postgres`, `template0`, `template1`, malformed connection strings, and missing authorization. The application database and the integration-test database must remain separate.

### Independent Python contract example

Python 3.12 and the standard library are sufficient to run the isolated
[Python authorization contract example](python_contract/README.md):

```bash
python3 -m unittest discover -s python_contract/tests -v
```

The example has a simulated protected operation, strict fail-closed response
parsing, and a live proof script that creates its own disposable test grants.
It is separate from any AI agent or application business logic.

### Service authentication

Send `Authorization: ApiKey <base64-key>` on every request. The check key has only the `permissions.check` capability and can call `POST /api/permissions/check`. The separate admin key has only `permissions.admin` and can grant or revoke tuples and use every `/api/roles` endpoint, including reads. Other endpoints require an authenticated service by default. Missing or invalid credentials receive HTTP 401; a valid key without the required capability receives HTTP 403.

The key authenticates the **calling service**. It does not authenticate the end user named by `subjectType` and `subjectId` in a check request. A caller must establish the end user's identity in its own trusted authentication flow and set those fields in server-side code. The authenticated checking service supplies asserted ABAC values from authoritative application data. For AgentFence, the Python backend must construct those values from trusted user and resource records; tool arguments, prompts, model output, and other LLM-generated text must never select or override the subject, resource identity, operation identity (`objectType` and `relation`), or security attributes. Matching nested identities only checks internal request consistency; it does not prove attribute authenticity or protect against a compromised Python backend or compromised checking service. Keep the check key only in that trusted backend, and restrict the admin key to administrative processes.

---

## API

Permission operations use POST with structured request bodies. Role reads use GET.

### Grant a permission

```http
POST /api/permissions/grant
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "7",
  "relation": "member",
  "objectType": "role",
  "objectId": "editor"
}
```

### Grant role-based access (with SubjectRelation)

```http
POST /api/permissions/grant
Content-Type: application/json

{
  "subjectType": "role",
  "subjectId": "editor",
  "relation": "viewer",
  "objectType": "report",
  "objectId": "42",
  "subjectRelation": "member"
}
```

This creates the tuple `report:42#viewer@role:editor#member` - anyone who is a member of the editor role can view report 42.

A tuple's identity consists of the object type and ID, relation, subject type and ID, and optional subject relation. A missing or null `subjectRelation` identifies a direct tuple; a non-null value identifies a separate userset tuple. Blank non-null subject relations are invalid.

Granting a new tuple returns HTTP 204. Repeating an active grant with the same expiration (including two null expirations) also returns 204 without changing its effective creation time. An expired tuple is reactivated with a new effective creation time and the requested expiration. An active tuple with a different requested expiration returns HTTP 409; revoke it first if a change is intended. An invalid, non-UTC, or already expired timestamp returns HTTP 400. Null `expiresAt` means a non-expiring grant. Expiration is normalized to PostgreSQL's microsecond precision and checked against the PostgreSQL clock when the grant is written, including after waiting for another writer. Permission checks continue to ignore expired grants.

### Check a permission

```http
POST /api/permissions/check
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "7",
  "relation": "viewer",
  "objectType": "document",
  "objectId": "42"
}
```

Response:

```json
{ "allowed": true, "reason": "Allowed via tuple expansion", "reasonCode": "authorization_allowed" }
```

This example assumes a matching `document:42#viewer@user:7` tuple. Documents
have no required ABAC registration in this example, so the check is ReBAC-only.
The same request for `report/viewer` now returns HTTP 400 without required ABAC
attributes, even if a matching tuple exists.

### Check with ABAC attributes

```http
POST /api/permissions/check
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "7",
  "relation": "viewer",
  "objectType": "report",
  "objectId": "42",
  "subjectAttributes": {
    "subjectType": "user",
    "subjectId": "7",
    "region": "us-east",
    "clearanceLevel": 3
  },
  "resourceAttributes": {
    "resourceType": "report",
    "resourceId": "42",
    "region": "eu-west",
    "sensitivityLevel": 2,
    "status": "draft"
  }
}
```

Response:

```json
{ "allowed": false, "reason": "Denied by ABAC policy: RegionMatchPolicy", "reasonCode": "required_abac_denied" }
```

For checks, HTTP 200 with `allowed: false` is an authorization denial. Missing or
inconsistent required attributes return HTTP 400. Policy evaluation or storage
failures return an error, not an allow response. The calling service must treat
every non-successful check response as denied access. Existing `report/viewer`
and `report/editor` callers must now supply the required attributes; other
unlisted operations retain ReBAC-only behavior.

### Check result and decision audit contract

Every request that reaches the check action receives an `X-Authorization-Decision-Id`
response header. A single structured `AuthorizationDecision` log event (event ID
`1001`) carries that ID, authenticated service identity, checked subject and
object type/ID and relation, `Outcome` (`allow`, `deny`, or `error`), `ReasonCode`,
`DurationMs`, and `HttpStatus`. A client disconnect can have `HttpStatus=0` because
no completed HTTP response is guaranteed. Authentication and JSON-binding failures
occur before this action and have no decision event or decision ID. They are not
completed authorization decisions.

HTTP 200 preserves `allowed` and `reason` and adds `reasonCode`. The stable codes
are `authorization_allowed`, `relationship_denied`, and `required_abac_denied`.
Malformed authorization context handled by the action returns HTTP 400 with
`reasonCode: invalid_authorization_context`; evaluation or database failure
returns HTTP 500 with `reasonCode: authorization_evaluation_error`. These errors
do not contain `allowed: true` or internal exception details. The existing `reason`
text is explanatory; consumers should use `allowed`, HTTP status, and `reasonCode`
for decisions. Allow a protected action only after HTTP 200 with a valid response
containing `allowed: true`; deny on any other response, timeout, or connection error.

Decision logs contain the checked identifiers, which may be sensitive metadata.
Use opaque identifiers in requests, restrict log access to authorization service
operators, and set a documented retention period in the deployment logging sink
(30 days is a reasonable starting point for this project). Do not place names,
emails, credentials, or secrets in identifier fields. The decision event omits API
keys, raw ABAC attributes, document content, and exception text. This service does
not itself enforce log retention or authenticate the asserted end-user identity.
Configure the logging sink to retain `Information` events from
`Permissions.Api.Controllers.PermissionsController`; filtering those events out
disables decision audit records.

Role-parent assignment returns HTTP 404 for a missing role or parent, HTTP 409
for a hierarchy conflict such as a cycle, and HTTP 204 on success. Role creation
with a missing parent also returns HTTP 404. Internal hierarchy failures return
HTTP 500 without exception details.

### Revoke a permission

```http
POST /api/permissions/revoke
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "7",
  "relation": "viewer",
  "objectType": "report",
  "objectId": "42"
}
```

---

## Modelling permissions

The `report:42#viewer` checks below include this trusted ABAC context in every check request: `subjectAttributes: { subjectType: "user", subjectId: "7", region: "us-east", clearanceLevel: 3 }` and `resourceAttributes: { resourceType: "report", resourceId: "42", region: "us-east", sensitivityLevel: 2 }`. The tuple grants alone are insufficient for a report check; omitting these attributes returns HTTP 400.

### Direct access

```
grant: report:42#viewer@user:7
check: report:42#viewer@user:7 with the ABAC context above → allowed (direct match)
```

### Role-based access

```
grant: role:editor#member@user:7
grant: report:42#viewer@role:editor#member
check: report:42#viewer@user:7 with the ABAC context above → allowed (via role expansion)
```

### Role inheritance

```
-- editor's parent role is viewer (set via Role.SetParent)
-- admin's parent role is editor

grant: report:42#viewer@role:viewer#member
grant: role:admin#member@user:7
check: report:42#viewer@user:7 with the ABAC context above → allowed (via admin → editor → viewer inheritance)
-- A viewer member does not inherit editor or admin permissions.
```

Role creation with a parent and role reparenting take the same PostgreSQL
transaction-scoped advisory lock before validating the hierarchy. Validation
and persistence run in one `READ COMMITTED` transaction, so a writer that waited
for the lock sees the preceding writer's committed hierarchy. Failed or canceled
transactions release the lock. This guarantee covers service writers that use
this locking protocol; direct database writes and other writers that skip it can
still create invalid role graphs.

### Time-bounded access

```
grant: report:42#viewer@user:7 with expiresAt: "2026-12-31T23:59:59Z"
check after expiry: report:42#viewer@user:7 with the ABAC context above → denied (tuple expired)
```

### Group membership

```
grant: group:finance#member@user:carol
grant: group:finance#member@user:dave
grant: budget:7#editor@group:finance#member
check: budget:7#editor@user:carol → allowed (carol is in finance group)
check: budget:7#editor@user:dave  → allowed (dave is in finance group)
check: budget:7#editor@user:eve   → denied  (eve is not in finance group)
```

---

## Design decisions

### Why POST for revoke instead of DELETE?

The permission tuple is identified by six fields, including nullable `subjectRelation`. Omitted or null `subjectRelation` revokes only a direct tuple. To revoke a userset tuple, supply its exact `subjectRelation`. Revoking an absent or expired tuple returns HTTP 204; an expired tuple is removed if present. Embedding all fields in a URL produces fragile, hard-to-read routes. Structured request bodies are consistent across all three operations.

The tuple-identity migration creates the new six-column unique index before removing the old five-column index. It preserves existing rows and expiration values. Existing duplicate direct tuples cause the migration to fail safely and must be reviewed before retrying. A downgrade can fail safely if distinct userset tuples created under the new identity collide under the old five-column index; it does not delete or merge grants.

### Why no Parse() on TupleKey?

String parsing is fragile. A typo in `report:42#viewer@user:7` fails silently at lookup time. A structured object fails loudly at deserialization. `ToString()` is kept for logging where all API transport uses structured DTOs.

### Why remove RoleAssignment? (in the older commits)

Two mechanisms for the same concept always leads to inconsistency. `RoleAssignment` and `RelationTuple` could both grant the same permission. By expressing role membership as tuples (`role:editor#member@user:7`), there is one source of truth and one algorithm for everything.

### Why sealed classes and records?

Domain entities are sealed to prevent inheritance from bypassing invariants enforced by private setters and factory methods. DTOs are sealed records for value equality and to signal they are not designed for extension.

### Why AsNoTracking() on all read queries?

Permission checks are read-heavy. EF Core's change tracking adds overhead for objects that will never be updated in the same context. `AsNoTracking()` removes that overhead for all read paths.

### Why factory methods instead of public constructors?

Entities can never be in an invalid state. `RelationTuple.Create()` validates all arguments before constructing. A public constructor would allow construction with empty strings. The private parameterless constructor exists only for EF Core materialisation.

---

## Known limitations and future work

**No caching** - each permission check makes multiple database round trips for deep expansions. A Redis cache with short TTL and tuple-aware invalidation would significantly improve throughput at scale. Google's Zanzibar uses a snapshot-based Leopard index for group membership and zookie consistency tokens for distributed freshness guarantees.

**No schema validation** - nothing prevents inserting `report:42#can-fly@user:7`. OpenFGA solves this with an authorization model that defines valid relations per object type. Adding a namespace schema layer would prevent invalid tuples at write time.

**No ListObjects endpoint** - "what can this user access?" requires reverse expansion - following tuples in the opposite direction. This is expensive without a dedicated index and is not currently implemented.

**No wildcard subjects** - `report:42#viewer@*` (public access) is not supported. The check engine would need to handle the `*` subject as a special case.

**No deny rules** - Zanzibar does not support negative permissions. ABAC policies provide contextual denial but there is no way to express "everyone except user:7 can view this." Deny rules would require a separate tuple type and additional check engine logic.

**Single region** - the system assumes a single PostgreSQL instance. A multi-region deployment would need per-region database clusters with tenant routing at the API gateway layer, and a control plane for tenant-to-region mapping.

---

## References

- [Zanzibar: Google's Consistent, Global Authorization System](https://research.google/pubs/pub48190/) - the original paper
- [OpenFGA](https://openfga.dev) - open source Zanzibar implementation by Auth0/Okta
- [Designing Data-Intensive Applications](https://dataintensive.net) - ch. 1-2 for data modeling foundations
- [Microsoft: Role-based access control in .NET](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
