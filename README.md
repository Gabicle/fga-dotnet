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

Step 3: Role inheritance - if checking a role object, walk parent chain
        role:editor parent is role:viewer
        Recursively check against parent role
```

A `HashSet<string>` tracks visited states to prevent infinite loops from circular role definitions.

**Layer 2 - ABAC policy evaluation**

If tuple expansion allows, attribute-based policies run as a second gate. All policies must pass:

```
RegionMatchPolicy     - subject.region must match resource.region
ResourceStatusPolicy  - deny if resource.status is "locked" or "archived"
SensitivityLevelPolicy - subject.clearanceLevel must be >= resource.sensitivityLevel
```

ABAC is optional per request - if no attributes are provided, the tuple result is returned directly.

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
UNIQUE INDEX ix_relation_tuples_lookup
  ON relation_tuples (object_type, object_id, relation, subject_type, subject_id)

-- Subject-based queries ("what can this user access?")
INDEX ix_relation_tuples_subject
  ON relation_tuples (subject_type, subject_id)

-- Expiry cleanup
INDEX ix_relation_tuples_expires_at
  ON relation_tuples (expires_at)
```

---

## Running locally

**Prerequisites:** .NET 10, Docker

```bash
# Start PostgreSQL
docker run --name permissions-db \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_DB=permissions \
  -p 5432:5432 -d postgres:16

# Apply migrations
dotnet ef database update \
  --project Permissions.Infrastructure \
  --startup-project Permissions.Api

# Run the API
dotnet run --project Permissions.Api
```

API available at `http://localhost:5031`. OpenAPI schema at `http://localhost:5031/openapi/v1.json`.

---

## API

All operations use POST. Structured request bodies rather than URL parameters - the same approach taken by OpenFGA.

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

### Check a permission

```http
POST /api/permissions/check
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "7",
  "relation": "viewer",
  "objectType": "report",
  "objectId": "42"
}
```

Response:

```json
{ "allowed": true, "reason": "Allowed via tuple expansion" }
```

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
{ "allowed": false, "reason": "Denied by ABAC policy: RegionMatchPolicy" }
```

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

### Direct access

```
grant: report:42#viewer@user:7
check: report:42#viewer@user:7 → allowed (direct match)
```

### Role-based access

```
grant: role:editor#member@user:7
grant: report:42#viewer@role:editor#member
check: report:42#viewer@user:7 → allowed (via role expansion)
```

### Role inheritance

```
-- editor's parent role is viewer (set via Role.SetParent)
-- admin's parent role is editor

grant: role:admin#member@user:7
check: report:42#viewer@user:7 → allowed (via admin → editor → viewer inheritance)
```

### Time-bounded access

```
grant: report:42#viewer@user:7 with expiresAt: "2026-12-31T23:59:59Z"
check after expiry: report:42#viewer@user:7 → denied (tuple expired)
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

The permission tuple is identified by five fields. Embedding all of them in a URL produces fragile, hard-to-read routes. Structured request bodies are consistent across all three operations and match the approach taken by OpenFGA.

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
