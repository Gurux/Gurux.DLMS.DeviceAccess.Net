# Gurux DLMS DeviceAccess

A shared .NET 9, .NET 10 and .NET 11 library for connecting to DLMS meters, reading association
views and attributes, writing values, invoking COSEM methods, reading profiles,
and transferring firmware. The transport is any compatible `IGXAsyncMedia`, including
an already open connection accepted by an AMI agent. Device configuration, UI,
application persistence remain in the calling application. Certificate generation can call a Gurux-compatible signing service.

## Licensing

This software is available under GPL-2.0-only or a separate commercial license
granted by Gurux Ltd. The complete GPL text is in [LICENSE](../LICENSE).
See [commercial licensing information](../LICENSES/LicenseRef-Gurux-Commercial.txt)
for the commercial option; its terms are supplied by Gurux Ltd in a written agreement.

## Open a session

```csharp
using Gurux.DLMS.DeviceAccess;
using Gurux.DLMS.Enums;
using Gurux.DLMS.Secure;
using Gurux.Net;

var client = new GXDLMSSecureClient(true, 16, 1,
    Authentication.None, null, InterfaceType.WRAPPER);
using var media = new GXNet { HostName = "127.0.0.1", Port = 4059 };
using var reader = new GXDLMSReader(client, media, new GXDLMSReaderOptions
{
    WaitTime = 5000, // Milliseconds; must be positive.
    RetryCount = 3,
    CheckAccessRights = true
});
reader.OnKeyPathRequested = SelectKeyFile; // The application's localized file chooser.

await reader.ConnectAsync(new GXDLMSConnectOptions
{
    WaitTime = 10000, // Connection setup timeout in milliseconds.
    RetryCount = 3,
    InvocationCounter = "0.0.43.1.0.255",
    IgnoreSNRMWithPreEstablished = false,
    ReadAssociationView = true
}, cancellationToken);
// Client.Objects now contains the association view.
```

`Connect`/`ConnectAsync` open a closed transport, perform SNRM/AARQ/HLS as needed,
and by default read a missing association view. Repeated calls reuse a connected
session. `Connect(new GXDLMSConnectOptions { ReadAssociationView = false })` skips discovery so manually constructed objects can be
used. An open accepted transport is reused rather than replaced.

Use `Connect`/`ConnectAsync` for all connection setup. To perform only the
handshake, pass `new GXDLMSConnectOptions { ReadAssociationView = false }`.
The former `InitializeConnection` and `InitializeConnectionAsync` methods have
been removed; update existing calls to `Connect` or `ConnectAsync` with this option.
`GXDLMSConnectOptions.WaitTime` and `RetryCount` apply while opening and
authenticating the connection. After setup, including association-view discovery,
operations use the constructor's `GXDLMSReaderOptions.WaitTime` and `RetryCount`.
Connection settings are copied and reused when reconnecting without new options.
Both option classes default to 5000 milliseconds and three rejected-packet attempts.

Call `EnsureAssociationView` or `EnsureAssociationViewAsync` to discover objects
later. Discovery is tracked independently from the number of objects: adding
objects manually does not mark the association as read. Set
`GXDLMSReaderOptions.AssociationViewLoaded = true` only when the caller has
supplied a previously read view. Pass `forceRefresh: true` to refresh it.
`GetAssociationView` optionally loads/saves a caller-selected XML cache.

`Close`/`CloseAsync` release and close the connection; the same reader can
reconnect. `Dispose` is terminal and detaches callbacks. The caller owns the
client and media; the reader closes the media but does not dispose it.

## Attributes and actions

```csharp
object value = await reader.ReadAsync(target, 2, cancellationToken: cancellationToken);
await reader.WriteAsync(target, 2, newValue, cancellationToken: cancellationToken);
object result = await reader.MethodAsync(target, 1, parameter, parameterType,
    cancellationToken: cancellationToken);
```

Local rights checks default to `CheckAccessRights`. Override the default for
one operation with `checkAccessRights: true` or `false`:

```csharp
reader.CheckAccessRights = false;
object value = reader.Read(manuallyAddedObject, 2);
reader.Write(manuallyAddedObject, 2, newValue, checkAccessRights: false);
reader.Method(manuallyAddedObject, 1, parameter, parameterType, checkAccessRights: false);
// Enforce the local check for this operation even if globally disabled:
reader.Read(target, 2, checkAccessRights: true);
```

Bypass is useful when no association has been read and local rights are
unknown. The meter still enforces its permissions. A local denial throws
`UnauthorizedAccessException`; a meter denial propagates as `GXDLMSException`.
List operations validate all requested rights before sending anything.

Write and WriteAsync each have two overloads: one writes the object's current
attribute value, and the other accepts a candidate value. A candidate is applied
to the original object only after the meter accepts it. A successful write
clears the attribute's dirty flag.

~~~csharp
await reader.WriteAsync(target, 2, cancellationToken: cancellationToken);
await reader.WriteAsync(target, 2, checkAccessRights: false);
await reader.WriteAsync(target, 2, newValue,
    checkAccessRights: false, cancellationToken: cancellationToken);
reader.Write(target, 2); // Synchronous; blocks until completion.
~~~

For the current-value overload, the optional cancellation token comes before
the optional rights override. Specify checkAccessRights by name; positional
boolean third arguments always represent attribute values. The former
WriteCurrentValue and WriteCurrentValueAsync names have been removed; migrate
those calls to Write and WriteAsync, using named arguments for rights and
cancellation. An untyped third-position default selects the current-value overload;
use value: default to explicitly write a null candidate.

The method overload accepting `Func<GXDLMSClient, byte[][]>` supports existing
typed COSEM action builders. `Exchange`/`ExchangeAsync` provide an unrestricted
protocol escape hatch; the caller is responsible for access checks there.
Request creation inside these callbacks runs under the session gate.
`ReadRowsByEntry`/`ReadRowsByRange`, and their async counterparts, support profile
reads; a failed read restores the previous buffer.

## Firmware

```csharp
await reader.ImageUpdateAsync(imageTransfer, identifier, firmware,
    new GXImageUpdateOptions
    {
        Verify = true,
        Activate = true,
        Resume = true,
        VerificationTimeout = TimeSpan.FromMinutes(30),
        ActivationTimeout = TimeSpan.FromMinutes(30),
        OnProgress = progress => ReportProgress(progress)
    }, checkAccessRights: false, cancellationToken: cancellationToken);
```

The library reads the enabled flag and block size, initiates the transfer,
sends blocks, checks the received-block bitmap and retries missing blocks with
a bounded retry count. It waits for verification and activation status rather
than assuming an action acknowledgment means completion. TemporaryFailure
is accepted while these phases are in progress. Failed status, missing image
identity, cancellation and deadlines stop the update.

Resume requires the meter to expose the matching identifier and image size
while transfer is in progress. Meters that hide this metadata until verification
start a fresh transfer. Activation recovery reconnects after transport loss or
DisconnectMode, without repeating the activation action. Successful activation
requires the meter to confirm `ActivationSuccessful` after reconnecting.
With `Verify = false, Activate = false`, the update performs transfer only.

## Callbacks and concurrency

Each reader serializes its operations, including request generation and mutable
DLMS invoke/block counters. Use one reader per client/connection. Cancellation
is checked while waiting for a response or an operation slot. A canceled or
incomplete exchange closes the transport so its reply cannot be reused by the
next operation. The reader requires `IGXAsyncMedia` and awaits transport opening,
sending, receiving and closure directly. Synchronous methods call the same async
implementation and block until it completes. Use async methods in UI applications.
Buffered receives use `BeginSynchronous()` scopes that remain valid across awaits.
`DisposeAsync` closes and releases reader callbacks without blocking a thread.
Certificate-service HTTP communication reuses pooled connections and supports cancellation.
`GXDLMSReaderOptions.CertificateServiceTimeout` bounds each complete signing-service
request, including response-body reads (default: 100 seconds).
`CertificateServiceMaxResponseBytes` limits the complete JSON response (default:
1 MiB), including chunked responses without Content-Length. Both settings are copied
at reader construction and must be positive; the timeout must not exceed
4,294,967,294 milliseconds. Oversized responses raise a localized
`InvalidOperationException`; deadline expiry raises `OperationCanceledException`.
XML/PEM serialization and cryptographic dependency calls remain synchronous;
cancellation is checked between processing stages.

Callbacks run inline on the current operation continuation; marshal UI interaction
when necessary and do not start another meter operation inside a callback.
The application handles `Client.OnKeys`, invokes `OnKeyPathRequested` on every
key request, and loads the selected PEM file. The reader does not subscribe to
`Client.OnKeys`. See the console example's `Program.Keys.cs`. No path is cached. A blank
result cancels selection; file errors propagate. `OnNotificationReceived`
provides reply metadata, and `OnNotification` provides the existing XML/PRIME
payload form. Consume notification data before returning from the callback.

`OnTrace` receives textual diagnostics when tracing is enabled;
`OnPacketTrace` receives request/response bytes, direction and UTC timestamp
when assigned. There is no implicit console or trace-file output. Applications
control where logs are stored. Firmware progress uses `OnProgress`.
Exceptions from individual `OnTrace`, `OnPacketTrace` and `OnProgress` subscribers
are ignored; remaining subscribers still receive the report and the meter operation
continues. This includes subscriber-thrown cancellation exceptions: cancel using the
operation's cancellation token. Callbacks still run inline and must return promptly.
Notification, key selection and initialization callback exceptions continue to propagate.

## Localization

User-facing errors and trace messages are stored in `Properties/Resources.resx`.
English is the neutral language. Add translations using matching resource files,
such as `Properties/Resources.fi.resx`, and keep the same keys and formatting
placeholders. Translator comments describe each placeholder's meaning.

The current UI culture selects resources; the current culture formats values.
Complete messages are formatted with `string.Format(CultureInfo.CurrentCulture, ...)`.
Protocol identifiers, payloads and raw TX/RX trace labels remain technical data.
The application supplies its own localized key-file chooser and other UI captions.

The build generates the strongly typed resource class under `obj/<configuration>/<framework>/Resources.Designer.cs`.
Each target framework has its own output. Visual Studio may also maintain a designer
under Properties; that source copy is excluded from compilation. Edit the .resx file, not generated classes.

## Verify

```text
dotnet build Gurux.DLMS.DeviceAccess.Net.sln -c Release --no-incremental -warnaserror
dotnet run --project Tests/DeviceAccess.Tests.csproj -p:TreatWarningsAsErrors=true
```

The console regression suite uses real Gurux DLMS server parsing over a
fragmentable in-process transport. It does not require a physical meter.

Certificate generation and export require an explicit root output directory:

```csharp
reader.GenerateCertificates(securitySetup, outputDirectory);
reader.ExportMeterCertificates(securitySetup, outputDirectory);
```

The Gurux Keys, Keys384, Certificates and Certificates384 subdirectories are
created beneath that directory. Relative paths are resolved at method entry.

`ReadAll(outputFilePath, settings)` reads available values, saves the objects to
XML, and closes the connection. The optional `GXXmlWriterSettings` controls
the final export; by default only the association view is saved with
`Values = false`. Set `Values = true` to include read attribute values. The same path is used for the association cache before readout; pass
null to disable file access.

Optical initialization timings are configured through `GXDLMSConnectOptions`.
All values are milliseconds: `OpticalHeadInitialDelay` (1000),
`OpticalHeadSwitchDelay` (200), `OpticalHeadReadyDelay` (800),
`OpticalHeadEchoWaitTime` (2000), and `OpticalHeadInvalidResponseWaitTime` (100).
Delays may be zero; receive timeouts must be positive. The initial optical
identification reply uses `WaitTime`. Custom `InitializeMedia` or `InitializeMediaAsync` callbacks
replace the built-in optical initialization and manage their own timing.

## Host-controlled Mode E transport

The library depends on Gurux.Common and Gurux.DLMS. Concrete transports such as
Gurux.Serial belong to the application. Configure reader.OnModeE before using
built-in HdlcWithModeE negotiation. Its signature is
Func<IGXMedia, ModeEOperation, int, CancellationToken, Task>.

| Operation | Host responsibility | Baud-rate argument |
| --- | --- | --- |
| InitModeE | Set 7E1 before the reader opens the transport. | 300 |
| SwitchModeE | Apply 8N1, closing and reopening if necessary; leave the transport open. | Negotiated rate |
| DisconnectModeE | Send SOH B0 ETX CR LF and consume the response. The reader sends no additional IEC disconnect. | 0 |
| RecoverModeE | Disable DTR/RTS, set the recovery rate, then enable DTR/RTS. | 9600 |

The reader awaits every callback before continuing and retains optical buffered
receive mode around negotiation and disconnect. The callback must use the
supplied media directly, honor cancellation and choose bounded receive timeouts.
It must not call reader session methods. No UI synchronization context is
guaranteed. Exceptions propagate and failed connection setup closes the media.
An already-open transport must already have the initial optical settings.

For example, an application referencing Gurux.Serial can register:

~~~csharp
reader.OnModeE = async (media, operation, baudRate, cancellationToken) =>
{
    var serial = (Gurux.Serial.GXSerial)media;
    var transport = (Gurux.Common.IGXAsyncMedia)media;
    switch (operation)
    {
        case ModeEOperation.InitModeE:
            serial.BaudRate = baudRate;
            serial.DataBits = 7;
            serial.Parity = Gurux.Serial.Parity.Even;
            serial.StopBits = Gurux.Serial.StopBits.One;
            break;
        case ModeEOperation.SwitchModeE:
            await transport.CloseAsync().ConfigureAwait(false);
            serial.BaudRate = baudRate;
            serial.DataBits = 8;
            serial.Parity = Gurux.Serial.Parity.None;
            serial.StopBits = Gurux.Serial.StopBits.One;
            await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            break;
        case ModeEOperation.DisconnectModeE:
            await transport.SendAsync(
                System.Text.Encoding.ASCII.GetBytes("\u0001B0\u0003\r\n"),
                null, cancellationToken).ConfigureAwait(false);
            var reply = new Gurux.Common.ReceiveParameters<string>
            {
                Eop = "\n",
                AllData = true,
                Count = 1,
                WaitTime = 5000
            };
            await transport.ReceiveAsync(reply, cancellationToken).ConfigureAwait(false);
            break;
        case ModeEOperation.RecoverModeE:
            serial.DtrEnable = serial.RtsEnable = false;
            serial.BaudRate = baudRate;
            serial.DtrEnable = serial.RtsEnable = true;
            break;
    }
};
~~~

Other interfaces do not require this callback. An existing InitializeMedia or InitializeMediaAsync
callback still replaces built-in optical initialization and does not require
OnModeE.
## Asynchronous initialization and cancellation

Choose one custom initializer in GXDLMSReaderOptions. InitializeMedia executes
synchronously; InitializeMediaAsync returns a Task that the reader awaits before
association traffic. Both replace built-in Mode E initialization, so neither
requires OnModeE. Setting both initializers is rejected at construction. The
reader copies the callback; later changes to the options do not affect it.

~~~csharp
var options = new GXDLMSReaderOptions
{
    InitializeMediaAsync = async token =>
    {
        await ConfigureTransportAsync(token).ConfigureAwait(false);
    }
};
~~~

Callbacks run under the session gate without a guaranteed UI context. Operate
on the caller-owned transport, honor cancellation, apply bounded I/O timeouts
and avoid invoking reader session methods from an initializer. OnTrace,
OnPacketTrace, OnNotificationReceived and progress callbacks are synchronous;
OnModeE and InitializeMediaAsync are awaited. Synchronous reader methods block
until completion and should not be called from a UI thread.

Existing certificate methods remain available. Cancellation-aware overloads
accept the token after the output directory; the signing-service address is
optional after the token:

~~~csharp
await reader.GenerateCertificatesAsync(
    securitySetup, outputDirectory, cancellationToken);
await reader.ExportMeterCertificatesAsync(
    securitySetup, outputDirectory, cancellationToken);
~~~

Cancellation covers waiting for the session, host initialization, meter I/O and
certificate-service HTTP communication. The complete certificate workflow holds
the session gate to prevent interleaving meter requests. File serialization and
cryptographic dependency calls remain synchronous: an already-running call
finishes before cancellation is checked. Export closes its connection on
completion or failure; canceled exchanges invalidate and close the transport.
The synchronous cancellation-aware overloads have the same blocking behavior
as the existing synchronous methods.

The library builds for both supported targets without generating packages:

~~~powershell
dotnet build Gurux.DLMS.DeviceAccess.Net.csproj -f net10.0 -p:GeneratePackageOnBuild=false -warnaserror
dotnet build Gurux.DLMS.DeviceAccess.Net.csproj -f net9.0 -p:GeneratePackageOnBuild=false -warnaserror
~~~

The console regression project currently targets net10.0.
## Source layout and nullable contracts

GXDLMSReader is a partial class. Its shared state, configuration, session gate
and disposal remain in GXDLMSReader.cs; the remaining files group related work:

| File | Responsibility |
| --- | --- |
| GXDLMSReader.Connection.cs | Association setup, optical negotiation, discovery and connection lifecycle |
| GXDLMSReader.Operations.cs | Attribute reads/writes, actions, lists and selective profile access |
| GXDLMSReader.Transport.cs | Buffered exchanges, packet processing and asynchronous media I/O |
| GXDLMSReader.Readout.cs | Complete readouts, profile/compact-data traversal and value tracing |
| GXDLMSReader.Certificates.cs | Meter-side certificate generation, import, export and verification |
| GXDLMSReader.ImageUpdate.cs | Firmware transfer, verification, activation and recovery |
| GXCertificateService.cs | Internal HTTP signing-service request and response handling |

The public reader API and its runtime signatures are unchanged by this split.
The certificate-service regression tests access its internal class through the
existing DeviceAccess.Tests friend assembly; they do not use reflection for HTTP
service calls. The reader class summary is maintained on its primary declaration;
member documentation stays beside each implementation.

Nullable analysis is enabled for both library targets. Optional callbacks,
initializers, cache paths and options are annotated as nullable. Attribute and
method values may be null where the COSEM type allows it. Null values in array
and list traces are rendered as empty fields while retaining their positions.
These annotations describe the existing API contract without introducing new
runtime parameter types.

The repository .editorconfig specifies CRLF, final newlines, indentation and
mandatory braces. EnforceCodeStyleInBuild enables build-time brace diagnostics,
and XML documentation warnings remain enabled. Generated sources are excluded
from style analysis. Existing files are not reformatted merely to apply settings.