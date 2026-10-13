# ADR-0020: The owner names the app; no app key

- **Status:** Accepted
- **Context:**
  - Design revisions 1 to 4 gave every UI setting and setting group an `AppKey`, for example `backoffice-web`, next to the owner.
  - The requester decided on 2026-10-13 (DRK-2190, round 3 answer 3, approved in the spec preview) that the owner already plays that part.
  - No UI setting code exists yet, so nothing has to be migrated.
- **Decision:**
  - A caller names the app through the owner it sends, for example owner `backoffice-web`.
  - `UiSetting` and `SettingGroup` have no `AppKey`.
  - At most 1 setting exists per owner and setting key. At most 1 setting group exists per owner and name.
  - A setting links only to a setting group of the same owner.
  - A caller that keeps layouts per user puts the user into the owner it sends, for example `backoffice-web-user-1`.
- **Alternatives:**
  - *Keep the app key next to the owner.* Rejected by the requester: 2 values for 1 idea.
  - *Make the app key required on every list.* Rejected: it falls away with the app key.
- **Consequences:**
  - Easier: 1 partition key for every record. No 409 for a group of another app.
  - Harder: settings of 2 apps for the same user need 2 owners. The caller builds that owner.
