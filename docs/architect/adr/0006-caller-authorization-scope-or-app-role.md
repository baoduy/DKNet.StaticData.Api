# ADR-0006: Authorize callers by scope or app role, 4 permissions

- **Status:** Accepted
- **Context:**
  - Callers use machine-to-machine tokens only (requester decision 10).
  - An Entra ID client-credentials token carries app roles in `roles`, not scopes. The local Keycloak puts scopes in `scope`.
  - DKNet.Accounts.Api's scope check reads only `scp` and `scope`, so an Entra client-credentials caller would be refused there.
  - DKNet.Notification.Api accepts its permission as a scope or an app role, and takes the caller id from `client_id`, `azp` or `appid`.
  - Reading files and changing them are different risks; so are files and UI settings.
- **Decision:**
  - 4 permissions: `files.read`, `files.write`, `settings.read`, `settings.write`.
  - Each is accepted in `scp`, `scope` (space-separated) or `roles`.
  - The caller id is the first of `client_id`, `azp`, `appid`. A token with none answers 401.
- **Alternatives:**
  - *Scopes only, as DKNet.Accounts.Api checks.* Rejected: Entra client-credentials callers would always get 403.
  - *One permission for everything.* Rejected: a UI backend that only saves layouts would also be able to delete files.
  - *A permission per route.* Rejected: too many to grant, with no risk they separate.
- **Consequences:**
  - Easier: works with Entra ID and the local Keycloak alike; least privilege per area.
  - Harder: operators define 4 app roles in Entra ID and grant them per caller.
