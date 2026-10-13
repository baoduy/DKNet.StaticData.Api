# ADR-0001: A new service for files and UI settings

- **Status:** Accepted
- **Context:**
  - Several services need to keep files, such as customer onboarding documents. Several UI apps need to remember screen layouts per user.
  - No DKNet service keeps files or UI settings today.
  - File storage needs its own secrets (storage credentials) and its own limits (50 MB uploads).
  - The requester asked for one central service for both areas, in its own repo, DKNet.StaticData.Api, and created it.
- **Decision:** Build DKNet StaticData as its own deployable service, in its own repo, scaffolded from DKNet.Templates. One service covers files and UI settings, delivered in slices.
- **Alternatives:**
  - *Grow DKNet.Accounts.Api.* Rejected: files and screen layouts are not ledger work. Every other caller would depend on the ledger service, and the ledger would hold storage secrets.
  - *A DKNet library that each caller embeds.* Rejected: every caller would hold storage secrets and its own tables, and the data would be split across services.
  - *Two services, one for files and one for settings.* Rejected by the requester: one service, two areas.
- **Consequences:**
  - Easier: one place for file storage rules, storage secrets and UI settings; a new caller needs only a token.
  - Harder: one more service to deploy and run; callers take an HTTP dependency and must handle 503.
