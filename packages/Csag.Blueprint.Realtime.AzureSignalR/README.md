# Csag.Blueprint.Realtime.AzureSignalR

## Overview

The Azure SignalR Service provider for the CSAG Blueprint real-time channel. It implements
`IRealtimeNotifier` and `IRealtimeTicketIssuer` from `Csag.Blueprint.Application` on Azure SignalR
Service in **serverless mode**.

The API never holds a connection. It grants a user membership of the tenant group and mints a client
token when the browser asks for a ticket, and it publishes over the service's REST API while it serves
the request that changed something. The browser keeps the only long-lived connection, to Azure SignalR.
That is what lets the API scale to zero.

## Registration

```csharp
services.AddBlueprintAzureSignalRRealtime(options =>
    configuration.GetSection("RealtimeSettings").Bind(options));
```

A host without real-time registers `NoOpRealtimeService` (from `Csag.Blueprint.Application`) for both
interfaces instead.

| Option | Default | Meaning |
| --- | --- | --- |
| `ConnectionString` | none | Access key authentication. Must carry `Endpoint` and `AccessKey`. Against the emulator, add `ClientEndpoint` when the API and the browser reach it under different hostnames. |
| `Endpoint` | none | Microsoft Entra ID authentication: the service's `https://<name>.service.signalr.net` URI. Requires `Credential`. |
| `Credential` | none | The `TokenCredential` for `Endpoint`. Set in code, because configuration cannot express it. |
| `HubName` | `app` | 1-64 letters, digits or underscores. |
| `TicketTokenLifetimeMinutes` | `60` | How long a client token stays valid. Must be shorter than the membership. |
| `GroupMembershipTtlHours` | `12` | How long a granted tenant membership lasts, 1 to 8760. |

The options are validated at startup. Exactly one of `ConnectionString` and `Endpoint` with
`Credential` must be set. The connection string goes through the SDK's own parser, so a malformed
value fails the start instead of the first request.

## Authentication

### Access key

`ConnectionString` signs every request and every client token with the service's access key. Use it
against the local emulator, which accepts nothing else.

### Microsoft Entra ID

`Endpoint` and `Credential` authenticate as the credential's identity instead of with the service's
access key, so the host is configured with no long-lived key and the service can run with local
authentication disabled. The host still holds key material at runtime: the SDK fetches a temporary key
from the service to sign client tokens, and keeps it in memory only (see below).

Bind `Endpoint` from configuration, and set `Credential` from the container. With
[Csag.WorkloadIdentity](https://www.nuget.org/packages/Csag.WorkloadIdentity) and its `AzureSignalR`
resource configured:

```csharp
services.AddBlueprintAzureSignalRRealtime(options =>
    configuration.GetSection("RealtimeSettings").Bind(options));

services.AddOptions<BlueprintAzureSignalROptions>()
    .Configure<IAzureSignalRTokenProvider>((options, tokenProvider) =>
        options.Credential = new WorkloadIdentityTokenCredential(tokenProvider));
```

On Azure, an `Azure.Identity` credential such as `ManagedIdentityCredential` works the same way.

The SDK requests the scope `https://signalr.azure.com/.default` and uses it two ways:

- Every publish and every group membership is a REST call with the credential's token as the bearer.
  The SDK asks the credential on each call and caches nothing, so give it a credential that holds its
  token, as `Azure.Identity` credentials and `Csag.WorkloadIdentity`'s `WorkloadIdentityTokenCredential`
  do.
- A client token has to be signed with a key. The first ticket waits while the SDK fetches a temporary
  key from the service (`GET /api/v1/auth/accessKey`), and every ticket is signed with it. The first
  ticket more than 55 minutes after a fetch starts a new fetch in the background. When a fetch fails
  and no usable key is left, tickets fail for 5 minutes before the SDK tries again.

The identity needs two built-in roles on the SignalR resource, because neither covers both uses:

| Role | Covers |
| --- | --- |
| **SignalR REST API Owner** | Sending to groups and users, and adding a user to a group |
| **SignalR App Server** | Fetching the temporary key (`auth/accessKey/action`) for client tokens |

**SignalR Service Owner** covers both alone, but also grants everything else on the data plane. A new
role assignment can take up to 30 minutes to take effect. Until then, publishing logs a warning and
tickets fail.

## Addressing

| Audience | Address on the service |
| --- | --- |
| Everyone in a tenant | group `t:{tenantId:N}` |
| One user in a tenant | user `{tenantId:N}:{userId:N}` |

The user address carries the tenant, so a person in two tenants receives a message only on the
connections opened while in that tenant. `RealtimeGroupNames` builds both.

## Delivery

- Messages go to the client method `Notify`, serialized in camelCase: `{ eventName, entityId, occurredAt }`.
- The transient transport sends each message as its own HTTPS request, with the SDK's retries on.
- A failed publish is logged as a warning and never thrown: the change that caused it is already committed.
