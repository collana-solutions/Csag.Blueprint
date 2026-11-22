---
"@collana-solutions/csag-blueprint-infrastructure": minor
"@collana-solutions/csag-blueprint-web": minor
---

Depend on the renamed Csag caching and Google Secrets packages instead of the deprecated Neolution ones

| Package | Up to 0.12.x | From this release |
| --- | --- | --- |
| Csag.Blueprint.Infrastructure, Csag.Blueprint.Web | `Neolution.Extensions.Caching.Distributed` 3.0.0 | `Csag.Extensions.Caching.Distributed` 4.0.0 |
| Csag.Blueprint.Web | `Neolution.Extensions.Configuration.GoogleSecrets` 1.6.0 | `Csag.Extensions.Configuration.GoogleSecrets` 2.0.0 |

The new packages are the same code under new IDs and namespaces.

**Breaking:** the distributed cache that `AddConfigurableDistributedCache` registers, and that the public constructors of `TicketCacheService`, `TranslationCacheInvalidator` and `TranslationProvider<TContext>` take, is now `Csag.Extensions.Caching.Abstractions.IDistributedCache<TCacheId>`. Applications get the new package through Csag.Blueprint.Infrastructure.

**To upgrade:**

1. Replace `using Neolution.Extensions.Caching.Abstractions;` with `using Csag.Extensions.Caching.Abstractions;` wherever the application uses `IDistributedCache<TCacheId>`, `CacheEntryOptions` or another type of that namespace.
2. If the application references a `Neolution.Extensions.Caching.*` package itself, replace it with the `Csag.Extensions.Caching.*` package of the same name at 4.0.0, or remove it. With both libraries loaded, `AddSerializedDistributedCache()` no longer compiles (CS0121, ambiguous call), and a service that asks for the old `IDistributedCache<TCacheId>` does not get the cache the Blueprint registers.
3. If the application references `Neolution.Extensions.Configuration.GoogleSecrets*` itself, replace it with `Csag.Extensions.Configuration.GoogleSecrets*` 2.0.0 and change `using Neolution.Extensions.Configuration.GoogleSecrets;` to `using Csag.Extensions.Configuration.GoogleSecrets;`.
4. The Google Secrets provider now logs to the console under the category `Csag.Extensions.Configuration.GoogleSecrets.GoogleSecretsProvider`. Update log queries and alerts that filter on `Neolution.Extensions.Configuration.GoogleSecrets.GoogleSecretsProvider`.

Unchanged: `AddGoogleSecretsIfConfigured`, the `Blueprint:GoogleSecrets:ProjectId` setting, the cache keys and the serialized format. Sessions, service account sessions and translations cached by 0.12.x stay readable after the upgrade, so users stay signed in. The EF model does not change.
