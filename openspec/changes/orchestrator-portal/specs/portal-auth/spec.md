# portal-auth — internal-only Entra ID access with a read/operate split

## ADDED Requirements

### Requirement: Entra ID sign-in, internal tenant only
The portal SHALL authenticate every request via OpenID Connect against the internal Entra ID tenant using a single app registration. Unauthenticated requests to any page or endpoint other than health probes MUST be redirected to sign-in; tokens from any other issuer/tenant MUST be rejected.

#### Scenario: Anonymous request is challenged
- **WHEN** an unauthenticated user requests any portal page
- **THEN** the portal redirects to Entra ID sign-in and serves no content

#### Scenario: Health probes stay anonymous
- **WHEN** the platform probes the health/liveness endpoints
- **THEN** the probes succeed without authentication and expose no stack data

### Requirement: App-role authorization separates reading from operating
Access SHALL require an app role: `Portal.Reader` grants all read views; `Portal.Operator` grants reads plus the operational verbs. An authenticated user with neither role MUST be denied with a clear message. Verb endpoints/handlers MUST enforce `Portal.Operator` server-side, independent of UI element visibility.

#### Scenario: Reader sees but cannot act
- **WHEN** a user holding only `Portal.Reader` opens a stack dashboard and attempts a verb (including by crafting the request directly)
- **THEN** all read views render and the verb is rejected server-side with a forbidden result

#### Scenario: Authenticated user without roles is denied
- **WHEN** a tenant user with no portal app role signs in
- **THEN** the portal denies access to all views with a message naming the required roles

### Requirement: Portal identity holds least-privilege, per-stack grants
The portal's managed identity SHALL be granted, per registered stack and via Terraform only: queue read/peek and message-send on the stack's queue, ACA job read and execution-stop, Log Analytics Reader on the stack's workspace, and get-access to that stack's dedicated read-scoped vendor secrets. The portal identity MUST NOT hold write access to vendor state, Key Vault write, or any grant on stacks absent from the registry.

#### Scenario: Grants match the registry
- **WHEN** the Terraform for the portal is applied for a set of registered stacks
- **THEN** the portal identity's role assignments enumerate exactly those stacks' queue, ACA, Log Analytics, and read-secret scopes, and nothing else

#### Scenario: Agent write secrets are inaccessible
- **WHEN** the portal attempts to read an agent write-capable secret (e.g. the GitHub App private key) from a stack's Key Vault
- **THEN** access is denied by Key Vault RBAC
