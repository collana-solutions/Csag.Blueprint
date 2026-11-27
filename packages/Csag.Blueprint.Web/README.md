# Csag.Blueprint.Web

## Overview

This package provides the **shared web-layer composition** for CSAG Blueprint-based applications.

It owns reusable validated options, builder and middleware composition helpers, FastEndpoints/Swagger setup, OAuth integration, correlation and request-culture infrastructure, and readiness support that applications can plug into their host.

## Core areas

### Options and validation

The package ships reusable option types and validators for:

- database startup behavior
- API/security settings
- cache provider settings
- feature flags
- localization settings

Use:

```csharp
services.AddBlueprintDefaultValidatedOptions(configuration);
```

This registers the package-owned validated options rooted under the `Blueprint` configuration section. It also
sets the session lifetime of `Csag.Blueprint.Infrastructure` (`BlueprintSessionOptions.Lifetime`) from
`Blueprint:Security:SessionExpirationHours`, the single setting the session cookie, its ticket and its tracking row
expire by; see the Infrastructure package's *Cookie sessions*. In the same way it sets
`BlueprintServiceAccountOptions` — signing key, issuer, audience, token lifetime and lockout — from
`Blueprint:Security:Jwt` and `Blueprint:Security:ServiceAccountLockout`; see the Infrastructure package's
*Service-account credentials*.

### Builder composition

`AddBlueprintServices(this WebApplicationBuilder, Action<BlueprintServicesOptions>? = null)` wires shared web services such as:

- HTTPS redirection / HSTS / security headers setup
- CORS configuration
- Google OAuth authentication
- FastEndpoints registration
- Swagger/OpenAPI registration
- distributed cache registration
- anti-forgery services
- the `TimeProvider` and the UTC `ITenantTimeZoneProvider` fallback, each with `TryAdd`

The optional `BlueprintServicesOptions` callback gives the application two hooks.
`FastEndpointsAdditionalAssemblies` finds endpoints and command/event handlers outside the entry
assembly. `ConfigureOpenApiDocument` sets extra OpenAPI document settings on top of the Blueprint
defaults, for example a custom `IDocumentProcessor`:

```csharp
builder.AddBlueprintServices(o =>
{
    o.FastEndpointsAdditionalAssemblies = [typeof(MyInfrastructureMarker).Assembly];
    o.ConfigureOpenApiDocument = s => s.DocumentProcessors.Add(new MyDocumentProcessor());
});
```

### Middleware composition

The package provides two main composition helpers:

```csharp
app.UseBlueprintSecurityHeaders();
app.UseBlueprintMiddleware();
```

`UseBlueprintMiddleware()` applies the shared request pipeline:

- HTTP audit logging
- correlation ID middleware
- CORS
- the refusal of a request that carries both a session cookie and a bearer token (400 ProblemDetails)
- authentication
- tenant middleware
- request localization
- authorization

Applications may still append app-specific middleware before endpoint mapping.

### Reusable middleware and services

| Component | Purpose |
| --- | --- |
| `CorrelationIdMiddleware` | Adds/propagates correlation IDs per request. |
| `SingleCredentialMiddleware` | Refuses a request that carries both the session cookie and a bearer token with a 400 ProblemDetails carrying the correlation ID, before authentication. Each request is authenticated by exactly one scheme. |
| `TenantMiddleware` | Establishes the ambient tenant context for the request. Delegates *how* the tenant is determined to `ITenantResolver`. |
| `HttpAuditMiddleware` | Records an audit event for a denied request (401 or 403 by default; configurable). `UseBlueprintMiddleware()` always registers it; `ConfigureBlueprintAuditLogging` turns it on. See [Audit enrichment](#audit-enrichment). |
| `CorrelationIdDelegatingHandler` | Propagates correlation IDs to outbound HTTP requests. |
| `SessionClaimRequestCultureProvider` | Resolves request culture from claims and `Accept-Language`. |
| `CultureNormalizationHelper` | Matches and validates requested cultures/languages. |
| `StartupCompletedHealthCheck` | Reusable readiness gate used with startup orchestration. |
| `TimeZoneDataHealthCheck` | Unhealthy when the host cannot resolve a well-known IANA zone (`Europe/Zurich`), i.e. the image has no time zone data; Degraded when it cannot resolve a legacy name browsers still send (`Asia/Calcutta`), i.e. Debian or Ubuntu without `tzdata-legacy`. Add it with `services.AddHealthChecks().AddBlueprintTimeZoneDataCheck()`: registered as `time_zone_data_check`, tagged `ready`. |

### Audit enrichment

`ConfigureBlueprintAuditLogging` adds the user ID, the email address, the display name, and the
correlation ID to each EF and HTTP audit event alike. It reads the three user values from the
claims on the request, not from the database. A service account has no email address, so its email
value is null and its display name is the account name from its token.

The provider writes `UserId`, `TenantId`, and `CorrelationId` to columns. The email address and the
display name stay in the `JsonData` column, at `$.UserEmail` and `$.UserDisplayName`. Reading either
value requires parsing the JSON data of the row.

Each EF entry records the entity's column values and its changes, not a copy of the entity object.
A field exclusion — the standard `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` and
`ClientSecretHash`, or one added through `BlueprintAuditOptions.EntityFieldConfigurator` — therefore
keeps the field out of the stored entry entirely. Do not call `IncludeEntityObjects()` in that
callback: Audit.NET serializes an included entity object whole, excluded fields and all.

`UseBlueprintMiddleware()` always registers `HttpAuditMiddleware`, first, so that it wraps
everything else in the pipeline: authentication and authorization, and also the exception handler
and status code pages. The ASP.NET Core authorization middleware does not call the next middleware
for a denied request, so a middleware registered later would never run for one. A middleware
registered after the exception handler would never see a status code that an unhandled exception
produced, so it could never audit one. An app does not need to register `HttpAuditMiddleware`
itself. Remove any manual `app.UseMiddleware<HttpAuditMiddleware>()` call; a duplicate registration
records two events per audited request.

An EF event's `TenantId` is the ambient `TenantContext.Current`, the tenant the actor has active, not
necessarily the tenant that owns the rows changed. A platform administrator who changes a global row
while a tenant is active therefore records the change under that tenant. Set
`BlueprintAuditOptions.EntityChangeTenantResolver` to decide per save: it receives the request (null
outside one) and the active tenant, and returns the tenant to record, or null for none. HTTP events
keep the tenant their request resolves to.

`ConfigureBlueprintAuditLogging` configures both audit features, the EF Core events and the HTTP
request events, from this one call; both are on by default. Its configure callback exposes the same
`HttpAuditOptions` instance the middleware reads, as `BlueprintAuditOptions.HttpAudit`; set
`HttpAudit.Enabled = false` there to turn HTTP request auditing off.
Until `ConfigureBlueprintAuditLogging` runs, or when that flag is `false`, the middleware does
nothing but call the next middleware. Once on, it writes an event for a request whose final status
code is 401 or 403 by default. Call `services.Configure<HttpAuditOptions>(o => ...)` to audit a
different set. It also writes the address in
`HttpContext.Connection.RemoteIpAddress`, as resolved by `ForwardedHeadersMiddleware`. That is the
nearest hop, which is the caller only when nothing between the caller and this app appends its own
`X-Forwarded-For` entry — see the forwarded-headers note under Rate limiting below. A request outside the audited set writes no event: the EF Core interceptor
already covers the writes, and GCP and Application Insights already record general request data.

`HttpAuditMiddleware` wraps the app's exception handler rather than running inside it, so it can
also audit a status code the handler itself produces, for example a 500 added to
`HttpAuditOptions.AuditedStatusCodes`. It catches and logs its own failure instead of throwing,
because sitting outside the exception handler means an unhandled exception here would reach the
ASP.NET Core default error handling instead of the app's configured one.

### Tenant resolution (the addressing seam)

`ITenantResolver` decides which tenant an incoming request belongs to. The package ships
`ClaimsTenantResolver` as the default, which reads the tenant from the authenticated session's
`TenantId` claim — "session-resolved" addressing, where the tenant is a property of who you are
signed in as rather than of the URL you requested.

Other generic addressing strategies — a vanity subdomain (`acme.example.com`), a path segment
(`/t/acme`), a header-driven tenant — belong in this package as additional `ITenantResolver`
implementations; if the one you need is missing, add it here rather than in your application. A
custom, app-local implementation remains possible for an addressing scheme that is genuinely
app-specific and where no generic resolver makes sense. Either way the default is registered with
`TryAddScoped`, so a resolver registered before `AddBlueprintServices` wins and the package default
never has to be unregistered:

```csharp
builder.Services.AddScoped<ITenantResolver, MyAppSpecificTenantResolver>();
builder.AddBlueprintServices();
```

Two things to know before switching addressing strategy:

- `TenantMiddleware` runs **after** `UseAuthentication`/`UseAuthorization`, because the default
  resolver needs the authenticated principal. A host- or path-based resolver does not, and moving the
  middleware earlier is what enables per-tenant branding and per-tenant identity-provider routing on
  the sign-in page.
- Sign-in currently *derives* the tenant and writes it into the session ticket. Once the URL is the
  source of truth that relationship inverts, so session composition needs revisiting too. The resolver
  is the seam, not the whole job.

The resolver returns `Guid?`; `null` means "no tenant context", which is a normal state for anonymous
requests, platform-scope endpoints, and users who belong to no tenant.

### FastEndpoints and Swagger

The package owns:

- FastEndpoints registration helpers
- conventional endpoint routing/naming helpers
- Swagger/OpenAPI registration helpers

`UseFastEndpointsWithConventions` gives every authenticated endpoint that declares no scheme of its own exactly one:
the session cookie (`CookieAuthMode = OptOut`), `Bearer` (`JwtAuthMode = OptOut`), or, with both, `SessionOrBearer`,
which picks the bearer token when the request carries one and the session cookie otherwise. An endpoint that declares
its own schemes with `AuthSchemes()` keeps exactly those.

Applications still own their endpoint classes, DTOs, validators, and policies.

`AddSwaggerDocumentation` registers `TemporalSchemaProcessor`, so the OpenAPI document describes what the API sends:

| CLR type | Schema |
| --- | --- |
| `DateTime`, `DateTime?` (a local reading, see `LocalDateTimeJsonConverter` in `Csag.Blueprint.Application`) | `type: string, format: date-time-local, pattern: ^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$` |
| `TimeOnly`, `TimeOnly?` | `type: string, format: time-local, pattern: ^\d{2}:\d{2}:\d{2}(\.\d{1,7})?$` |
| `TimeSpan`, `TimeSpan?` | `type: string`, the pattern of its constant (`c`) format `[-][d.]hh:mm:ss[.fffffff]`, no `format: duration` |

`date-time-local` and `time-local` are the names the [OpenAPI Format Registry](https://spec.openapis.org/registry/format/)
gives RFC 3339 date-times and times without the time zone component; NJsonSchema's defaults, `date-time` and `time`,
promise an offset. System.Text.Json writes a `TimeOnly` as `HH:mm:ss`, with a seven-digit fraction when it has one;
it also reads `HH:mm` and one-digit hours, which the pattern leaves out. The nullable forms add `nullable: true`.

`UseFastEndpointsWithConventions` binds `DateTime` and `DateTime?` from query, route, form and header values with the
same formats as a JSON body (`LocalDateTimeFormat`): a value with an offset, `Z`, a fraction, a culture format or nothing
is a 400. It binds `DateTimeOffset` and `DateTimeOffset?` with the same rule as a JSON body too (`InstantFormat`): an
instant must carry a UTC offset or `Z`, and a value without one, a date on its own or nothing is a 400. These 400s carry
FastEndpoints' binding failure message. An omitted nullable value binds null; an empty `DateTime?` or `DateTimeOffset?`
query or form value binds `default`, because FastEndpoints' `UseDefaultValuesForNullableProps` (on by default) assigns
empty nullable values without consulting the parser.

### Time zone validation

`TimeZoneRuleBuilderExtensions` adds FluentValidation rules next to `WithLocalizedMessage`:

| Rule | Requires |
| --- | --- |
| `MustBeIanaTimeZone()` | an IANA zone id this host resolves (`Europe/Zurich`, not `W. Europe Standard Time`); for stored zones; null passes |
| `MustBeWellFormedIanaTimeZone()` | a value of the IANA form, known to this host or not; for a zone a request names for itself, such as the browser's; null passes |
| `MustNotBeAfterToday(timeProvider, timeZone)` | a `DateOnly` not after today in the zone |
| `MustExistInTimeZone(timeZone)` | a local reading (`DateTime`) that is not in a spring-forward gap of the zone |

FastEndpoints validators are singletons, so the zone-dependent rules take a delegate that resolves the zone on every
validation, typically `(_, ct) => this.Resolve<ITenantTimeZoneProvider>().GetTimeZoneAsync(ct)`. They are
asynchronous: FastEndpoints calls `ValidateAsync`, and a synchronous `Validate` throws.

### Password screening

Passwords are screened at two layers:

| Layer | Screens against | Runs for |
| --- | --- | --- |
| `WithStrongPasswordRules` (FluentValidation) | length, the configured composition rules, the common-password list, `ContextSpecificWords` | every request validator that uses it, with a field-level message |
| `BlueprintPasswordValidator<TUser>` (Identity) | the common-password list, `ContextSpecificWords`, the account's email local part and user name, the breached-password check | every password `UserManager` sets: create, add, change and reset |

Register the Identity validator on the application's Identity builder:

```csharp
services.AddIdentity<ApplicationUser, ApplicationRole>(options => { /* password options */ })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddBlueprintPasswordScreening();
```

The breached-password check needs the account or a network lookup, so it runs only in the Identity validator, once
per password.

**Common passwords.** `CommonPasswordList` embeds 40,000 passwords of 8 or more characters: the 10,000 most frequent
of each length band (8-9, 10-11, 12-14, 15 or more). Under any minimum length from 8 to 15, at least 10,000 listed
passwords meet the policy. Matching ignores case. The list comes from the xato.net list of the one million most common
passwords (Mark Burnett, 2015), as [SecLists](https://github.com/danielmiessler/SecLists) distributes it under the MIT
license. `scripts/build-common-passwords.mjs` builds it from a pinned SecLists commit.

**Context-specific words.** `Blueprint:Security:PasswordSettings:ContextSpecificWords` lists the organisation,
product and application names a password must not contain. Matching ignores case, separators and common
substitutions (`C0ll4na-2026!` contains `collana`). Each word needs at least 3 letters or digits, which startup
validation enforces. The Identity validator also refuses the account's email local part and user name, whole or by
their parts of 4 or more letters (`anna.meier@example.com` refuses `Meier2026!`).

**Breached passwords.** `Blueprint:Security:PasswordSettings:BreachCheck` configures the check:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Mode` | `Disabled` | `Online` queries the [Pwned Passwords range API](https://haveibeenpwned.com/API/v3#PwnedPasswords); `Offline` reads range files from a local directory |
| `FailOpen` | `true` | accept the password when the lookup cannot complete (timeout, network or HTTP error, missing range file); `false` refuses it |
| `TimeoutMilliseconds` | `2000` | how long one online lookup may take (100 to 30000) |
| `RangeApiBaseUrl` | `https://api.pwnedpasswords.com/range/` | the range API for `Online`; an absolute https URL ending with `/` |
| `OfflineRangeDirectory` | none | for `Offline`: one file per range, named after the uppercase five-character prefix (`21BD1.txt`), each line `SUFFIX:COUNT`; the directory must exist at startup |

Only the first five characters of the password's SHA-1 hash leave the process (k-anonymity), and online requests ask
for response padding, so the response size does not reveal the range either. Neither the password nor its hash is
logged.

**Messages.** The rules use these translation keys, with an English fallback while the application does not define
them:

| Key | English fallback | Identity error code |
| --- | --- | --- |
| `Validation.PasswordTooCommon` | This password is too common. Choose one that is harder to guess | `PasswordTooCommon` |
| `Validation.PasswordContainsContextWord` | Password must not contain "{Word}" | `PasswordContainsContextWord` |
| `Validation.PasswordContainsAccountName` | Password must not contain your email address or user name | `PasswordContainsAccountName` |
| `Validation.PasswordBreached` | This password has appeared in a data breach. Choose a different one | `PasswordBreached` |
| `Validation.PasswordBreachCheckUnavailable` | The password could not be checked right now. Try again in a few minutes | `PasswordBreachCheckUnavailable` |

`PasswordScreeningErrorCodes` holds the error codes.

### Table view endpoints

`TableViewDataEndpointBase` and `TableViewExportEndpointBase` read the optional `timeZone` of their request (validated
by `TableViewDataRequestValidator` and `TableViewExportRequestValidator`) and fall back to `ITenantTimeZoneProvider`.
A well-formed `timeZone` this host's time zone data does not know falls back the same way, with a warning logged once
per id (ids longer than 64 characters are not well-formed);
`TableViewRequestTimeZone.TryFindRequested` applies that rule for an endpoint that handles these requests itself.
`TableViewExcelExporter` writes real date cells: a `DateOnly` as a date, a local reading as it reads, and an instant as
the reading in the effective zone including its time, with number formats from the request culture's date and time
patterns and its calendar (Buddhist years for `th-TH`, Persian dates for `fa-IR`). The headers of columns with a time
name their zone as an IANA id after the column header: an instant column the effective zone, so a fallback is visible
(`Created at (Europe/London)`), a local reading column the tenant's zone (`Start (Europe/Zurich)`); date columns name none.
A local reading column whose zone comes from its row (`TimeZoneColumn`) names none either: its rows can be in different
zones, and the zone column carries each row's zone into the workbook.

### Rate limiting

`RateLimiterExtensions` supplies the two pieces a host needs to wire up ASP.NET Core rate limiting;
the policies and their limits stay with the host.

`UseBlueprintRejectionResponse()` shapes the rejection: `429 Too Many Requests` instead of the
framework's `503 Service Unavailable` default (which an upstream load balancer reads as a backend
fault), an RFC 9457 ProblemDetails body carrying the correlation ID, and `Retry-After` when the
limiter reports it.

`TryGetClientPartitionKey(header, out key)` resolves the caller: the named edge-stamped header when
configured and parseable, else `Connection.RemoteIpAddress`, else nothing — the rule of
`ClientAddressHttpContextExtensions.TryGetClientAddress` in `Csag.Blueprint.Infrastructure`, which session tracking
records client addresses with too. It reports failure rather
than inventing a key, and a caller it cannot identify must be routed to
`RateLimitPartition.GetNoLimiter` — substituting a placeholder key puts every unidentifiable caller
in one bucket, so the first of them to exceed the limit rejects them all.

The key is opaque, not an address. An IPv4-mapped IPv6 address collapses to its IPv4 form so one
caller cannot occupy two partitions, and a native IPv6 address is truncated to its `/64` prefix and
returned as `2001:db8:85a3:8d3::/64`. IPv6 is allocated by prefix, not by address: the smallest block
normally routed to a customer is a /64, and its 2^64 addresses are all theirs to send from at no
cost, so keying on the full /128 enforces nothing against a caller who varies the source address
within their own prefix, while an IPv4 caller behind NAT shares one bucket with their whole office.
Truncation does not stop an attacker who rents several prefixes; it prices the evasion at one prefix
per bucket instead of at nothing. `HttpAuditMiddleware` records `Connection.RemoteIpAddress` itself,
so audit events keep the exact address.

Prefer an edge-stamped header. `RemoteIpAddress` is the nearest hop, not the caller: `ForwardLimit 1`
reads the rightmost `X-Forwarded-For` entry, and an edge that appends its own — a Google external
load balancer sends `<client>,<balancer>` — leaves that edge's address rightmost. Behind a further
reverse proxy it is that proxy's egress address: one constant shared by every caller. Reading the
header from the left instead is worse, because the entries a client wrote are kept there unverified.
The header must be one the edge writes itself and overwrites inbound, so a client cannot forge it.

All of that assumes the app is reachable only through that edge, which `UseBlueprintSecurityHeaders`
requires rather than enforces. It registers `ForwardedHeadersMiddleware` with `KnownProxies` and
`KnownIPNetworks` empty — Cloud Run and similar platforms front the app from internal addresses that
are not on the default loopback list — and emptying both turns the middleware's known-proxy check
off, so it applies whatever the immediate peer sent. A caller able to open a connection to the app
directly is that peer: it writes `X-Forwarded-For` itself, which makes the rightmost entry its own
value, and a forged `X-Forwarded-Proto: https` satisfies the HTTPS redirection and HSTS middleware.
Block direct ingress at the platform. The options are built inline rather than read from
`IOptions<ForwardedHeadersOptions>`, so a host cannot narrow them by configuration, and registering a
second `ForwardedHeadersMiddleware` is not a substitute: each run consumes the entries it reads.

## Ownership Boundary

This package owns **reusable web composition**, not the application host itself.

The consuming application still owns:

- `Program.cs`
- endpoint implementations
- app-specific validators and option extensions
- host-specific runtime services
- concrete authentication/authorization decisions at the app level
