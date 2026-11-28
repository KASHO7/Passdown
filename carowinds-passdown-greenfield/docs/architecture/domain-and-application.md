# Domain and Application

Current-state guide — see [review baseline](../architecture.md).

Domain owns deterministic operating rules, date/product identities and validation. Application owns use cases and repository contracts; `AssociatePassdownService` composes cached official data and date-specific drafts. Date selection is local-only; explicit Check Online uses the existing refresh path. Keep official values independent of manual display overrides, ERT and included-note copies. Dynamic seasons, operating events, Current Deals, product details and Pass Perks are represented in the Phase 31/32 records. Manager remains deferred.

Verification states (`AssociateVerificationState`) project `LastKnownGood` per category and product. Pre-RC.15 added `PreviouslyVerified`: the stored value was verified for that exact date and the latest successful check found the date no longer offered (`VerifiedDatedPriceRetention`; `LastKnownGood.RecordNoOfferAttemptRetainingVerified` in Domain). It is distinct from `Stale` (latest check failed) and is never treated as `Verified`; the Dashboard source status maps it to `Unavailable`, the PDF footer flag treats it as not recently verified. See [the fix record](../pre-rc15-post-closing-pricing-retention.md).

## Evidence and detailed implementation records

- [phase31-dynamic-seasons-and-deals.md](../phase31-dynamic-seasons-and-deals.md)
- [phase31b-product-details-price-history-ert-sync.md](../phase31b-product-details-price-history-ert-sync.md)
- [phase32-monthly-pass-perks-calendar.md](../phase32-monthly-pass-perks-calendar.md)
