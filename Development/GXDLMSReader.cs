// --------------------------------------------------------------------------
// Gurux Ltd
//
// Copyright (c) Gurux Ltd.
//
// This file is part of Gurux Device Framework.
//
// Commit: $Format:%H$
// Date:   $Format:%cI$
//
// This software is available under either of the following licenses:
//
// 1. GNU General Public License, version 2 only (GPL-2.0-only).
//    See the LICENSE file included with this distribution.
//
// 2. A commercial license granted by Gurux Ltd under a separate written
//    agreement. Use under this option requires a valid commercial license
//    and is governed by the terms of that agreement.
//
// If you have not obtained a commercial license from Gurux Ltd,
// use of this software is governed by GPL-2.0-only.
//
// Commercial licensing: https://www.gurux.fi/
//
// SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
// --------------------------------------------------------------------------

using Gurux.Common;
using Gurux.DLMS.Enums;
using Gurux.DLMS.Secure;
using System;
using System.Diagnostics;
using System.IO;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>
    /// Communicates with a DLMS/COSEM meter and provides association, readout, certificate, and firmware operations.
    /// </summary>
    public partial class GXDLMSReader : IDisposable, IAsyncDisposable
    {
        /// <summary>Snapshot of the timeout and retry settings used outside connection establishment.</summary>
        private readonly GXDLMSReaderOptions readerOptions;
        /// <summary>Snapshot of the connection settings reused for internal reconnects.</summary>
        private GXDLMSConnectOptions connectOptions = new GXDLMSConnectOptions();
        /// <summary>Whether the current serialized operation is opening or authenticating the connection.</summary>
        private bool connectionSetupActive;
        /// <summary>Gets the timeout selected for the current operation phase in milliseconds.</summary>
        private int WaitTime => connectionSetupActive ? connectOptions.WaitTime : readerOptions.WaitTime;
        /// <summary>Gets the maximum rejected-packet attempt count for the current operation phase.</summary>
        private int RetryCount => connectionSetupActive ? connectOptions.RetryCount : readerOptions.RetryCount;

        /// <summary>
        /// Caller-supplied transport used for meter communication.
        /// </summary>
        private readonly IGXAsyncMedia media;
        /// <summary>
        /// Caller-supplied protocol client used to encode requests and decode replies.
        /// </summary>
        private readonly GXDLMSSecureClient client;
        /// <summary>
        /// Trace verbosity selected at construction.
        /// </summary>
        private readonly TraceLevel Trace;
        /// <summary>
        /// Gets the caller-supplied communication transport.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This reader has been disposed.</exception>
        public IGXAsyncMedia Media
        {
            get
            {
                ThrowIfDisposed();
                return media;
            }
        }

        /// <summary>
        /// Gets the caller-supplied DLMS protocol client.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This reader has been disposed.</exception>
        public GXDLMSSecureClient Client
        {
            get
            {
                ThrowIfDisposed();
                return client;
            }
        }

        /// <summary>
        /// Gets or sets the application callback used to select a PEM file. Return a PKCS#8 file for
        /// encryption or an X.509 certificate file for decryption.
        /// The callback runs synchronously on the thread requesting the key;
        /// applications must marshal UI interaction to their UI thread if needed.
        /// A null or whitespace result cancels the request. No path is cached.
        /// </summary>
        /// <remarks>The application must handle the client's OnKeys event and invoke this callback; the reader does not subscribe to key requests.</remarks>
        public Func<GXCryptoKeyParameter, string?>? OnKeyPathRequested { get; set; }

        /// <summary>
        /// Optional callback invoked synchronously with a received PRIME concentrator notification or translated notification XML.
        /// </summary>
        public Action<object>? OnNotification;
        /// <summary>Gets or sets the asynchronous host callback that performs transport-specific Mode E operations.</summary>
        /// <remarks>
        /// <para>The arguments are the caller-supplied media, requested operation, baud rate and active cancellation token.
        /// The baud rate is 300 for initialization, the negotiated rate for switching, 9600 for recovery, and zero for disconnect.</para>
        /// <para>Initialization configures 7E1 before the reader opens the media. Switching configures 8N1 and must leave
        /// the media open; the host owns any required close/reopen. Recovery controls DTR/RTS and baud rate.
        /// Disconnect sends SOH B0 ETX CR LF and consumes the response; the reader does not duplicate that exchange.</para>
        /// <para>The callback is awaited while the reader session is serialized, without a guaranteed UI context.
        /// Use the supplied media directly rather than invoking reader operations. Honor the cancellation token and
        /// apply bounded I/O timeouts. Exceptions propagate to the caller and connection setup closes failed media.
        /// Built-in Mode E requires a handler; other interfaces and custom InitializeMedia or InitializeMediaAsync callbacks do not.</para>
        /// </remarks>
        public Func<IGXMedia, ModeEOperation, int, CancellationToken, Task>? OnModeE { get; set; }

        /// <summary>Serializes asynchronous session operations without holding a thread-affine monitor.</summary>
        private readonly SemaphoreSlim sessionSync = new SemaphoreSlim(1, 1);
        /// <summary>Tracks ownership of a serialized operation across asynchronous continuations.</summary>
        private readonly AsyncLocal<SessionLease?> sessionLease = new AsyncLocal<SessionLease?>();
        /// <summary>Cancellation token inherited by nested operations in the current session.</summary>
        private CancellationToken operationToken;
        /// <summary>Indicates that callbacks have been detached and this reader cannot be reused.</summary>
        private bool disposed;
        /// <summary>Tracks whether the DLMS association is usable independently of transport state.</summary>
        private volatile bool connected;
        /// <summary>Counts nested operations while the session gate is held.</summary>
        private int operationDepth;
        /// <summary>Stopwatch timestamp of the most recent transmitted frame, or zero before the first send.</summary>
        private long lastSend;
        /// <summary>Absolute stopwatch deadline for the current operation, or zero when only per-packet timeouts apply.</summary>
        private long operationDeadline;
        /// <summary>Minimum interval in milliseconds between transmitted frames.</summary>
        private int frameDelay;
        /// <summary>Optional caller-supplied transport initialization callback executed with the session gate held.</summary>
        private Action<CancellationToken>? initializeMedia;
        /// <summary>Optional host initializer awaited under the session gate before association requests.</summary>
        private Func<CancellationToken, Task>? initializeMediaAsync;
        /// <summary>Gets whether this reader is undisposed, the association is usable and the transport is open.</summary>
        public bool IsConnected => !disposed && connected && media.IsOpen;
        /// <summary>Gets whether the client association view has been supplied or successfully loaded.</summary>
        public bool AssociationViewLoaded { get; private set; }
        /// <summary>Gets or sets the default local access-rights check; each typed operation can override it.</summary>
        public bool CheckAccessRights { get; set; } = true;
        /// <summary>Gets whether connection setup skips SNRM for a pre-established association.</summary>
        private bool IgnoreSNRMWithPreEstablished => connectOptions.IgnoreSNRMWithPreEstablished;

        /// <summary>Gets or sets the nonnegative minimum interval in milliseconds between transmitted frames.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The assigned delay is negative.</exception>
        public int FrameDelay
        {
            get => frameDelay;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                frameDelay = value;
            }
        }

        /// <summary>Gets or sets the text trace callback invoked synchronously under the asynchronous session gate when tracing is enabled.</summary>
        /// <remarks>The callback is optional. A formatter that returns null can produce a null trace value. Each subscriber exception is ignored so diagnostics cannot interrupt communication or other subscribers. Use the operation cancellation token to cancel.</remarks>
        public Action<string?>? OnTrace { get; set; }
        /// <summary>Gets or sets the packet callback invoked synchronously under the session and buffered receive scopes.</summary>
        /// <remarks>Each subscriber exception is ignored so tracing cannot interrupt communication or other subscribers. Use the operation cancellation token to cancel.</remarks>
        public Action<GXPacketTrace>? OnPacketTrace { get; set; }
        /// <summary>Gets or sets the complete-notification callback invoked on the communication thread under the session and buffered receive scopes. Consume or copy the reply before returning because it is cleared afterwards.</summary>
        public Action<GXReplyData>? OnNotificationReceived { get; set; }

        /// <summary>Creates a reader using the caller-owned client, media and connection options.</summary>
        /// <param name="client">Caller-owned secure DLMS client whose mutable protocol state is serialized by this reader.</param>
        /// <param name="media">Caller-owned transport, which this reader opens and closes but does not dispose.</param>
        /// <param name="options">Connection settings and callbacks to apply to the reader.</param>
        /// <exception cref="ArgumentNullException">The client, media or options is null.</exception>
        /// <exception cref="ArgumentException">Both synchronous and asynchronous media initializers are configured.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An option contains an invalid wait time, retry count, frame delay, certificate-service deadline or response size limit.</exception>
        public GXDLMSReader(GXDLMSSecureClient client, IGXAsyncMedia media, GXDLMSReaderOptions options)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(media);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.WaitTime);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RetryCount);
            GXCertificateService.ValidateSettings(options.CertificateServiceTimeout, options.CertificateServiceMaxResponseBytes);
            readerOptions = new GXDLMSReaderOptions
            {
                WaitTime = options.WaitTime,
                RetryCount = options.RetryCount,
                CertificateServiceTimeout = options.CertificateServiceTimeout,
                CertificateServiceMaxResponseBytes = options.CertificateServiceMaxResponseBytes
            };
            Trace = options.TraceLevel;
            this.media = media;
            this.client = client;
            FrameDelay = options.FrameDelay;
            CheckAccessRights = options.CheckAccessRights;
            AssociationViewLoaded = options.AssociationViewLoaded;
            if (options.InitializeMedia != null && options.InitializeMediaAsync != null)
            {
                throw new ArgumentException(Resources.MediaInitializersConflict, nameof(options));
            }

            initializeMedia = options.InitializeMedia;
            initializeMediaAsync = options.InitializeMediaAsync;
            media.OnMediaStateChange += MediaStateChanged;
        }
        /// <summary>Rejects operations after this reader has been disposed.</summary>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
        /// <summary>Marks the association unusable when the transport starts closing or is closed.</summary>
        /// <param name="sender">Transport that raised the state-change event.</param>
        /// <param name="args">New transport state.</param>
        private void MediaStateChanged(object? sender, MediaStateEventArgs args)
        {
            if (args.State == MediaState.Closing || args.State == MediaState.Closed)
            {
                connected = false;
            }
        }

        // All public session operations enter the same reentrant asynchronous gate, including
        // request creation: the DLMS client's invoke/block counters are mutable.
        /// <summary>Runs an operation under the reentrant asynchronous session gate and propagates its cancellation token to nested exchanges.</summary>
        /// <typeparam name="T">Result type returned by the operation.</typeparam>
        /// <param name="action">Operation to execute while holding the asynchronous session gate.</param>
        /// <param name="token">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <param name="requireConnection">Whether an established association is required before invoking the operation.</param>
        /// <returns>A task returning the value returned by the operation.</returns>
        /// <remarks>Timeout, I/O failure or cancellation during an exchange invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested while waiting for the session or executing the action.</exception>
        /// <exception cref="InvalidOperationException">A connection is required but the association is unusable.</exception>
        /// <param name="allowDisposed">Whether cleanup may run after the reader has been disposed.</param>
        private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token = default, bool requireConnection = true, bool allowDisposed = false)
        {
            var inheritedLease = sessionLease.Value;
            bool ownsLease = inheritedLease == null || !inheritedLease.Active;
            SessionLease? lease = inheritedLease;
            if (ownsLease)
            {
                await sessionSync.WaitAsync(token).ConfigureAwait(false);
                lease = new SessionLease();
                sessionLease.Value = lease;
            }

            CancellationToken previous = operationToken;
            try
            {
                if (!allowDisposed)
                {
                    ThrowIfDisposed();
                }

                if (operationDepth == 0 || token.CanBeCanceled)
                {
                    operationToken = token;
                }

                operationToken.ThrowIfCancellationRequested();
                if (requireConnection && !IsConnected)
                {
                    throw new InvalidOperationException(Resources.ConnectionRequired);
                }

                ++operationDepth;
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception error) when (error is TimeoutException || error is IOException || error is OperationCanceledException)
                {
                    connected = false;
                    try
                    {
                        await media.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    throw;
                }
                catch (Exception error) when (IsAssociationLost(error))
                {
                    connected = false;
                    throw;
                }
                finally
                {
                    --operationDepth;
                }
            }
            finally
            {
                operationToken = previous;
                if (ownsLease && lease != null)
                {
                    lease.Active = false;
                    sessionLease.Value = inheritedLease;
                    sessionSync.Release();
                }
            }
        }

        /// <summary>Runs an operation under the reentrant asynchronous session gate and propagates its cancellation token to nested exchanges.</summary>
        /// <param name="action">Operation to execute while holding the asynchronous session gate.</param>
        /// <param name="token">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <param name="requireConnection">Whether an established association is required before invoking the operation.</param>
        /// <remarks>Timeout, I/O failure or cancellation during an exchange invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested while waiting for the session or executing the action.</exception>
        /// <exception cref="InvalidOperationException">A connection is required but the association is unusable.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        /// <param name="allowDisposed">Whether cleanup may run after the reader has been disposed.</param>
        private async Task ExecuteAsync(Func<Task> action, CancellationToken token = default, bool requireConnection = true, bool allowDisposed = false)
        {
            await ExecuteAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            }, token, requireConnection, allowDisposed).ConfigureAwait(false);
        }

        /// <summary>Determines whether a DLMS error reports disconnection or an unsupported association service.</summary>
        /// <param name="error">Exception to inspect for association loss.</param>
        /// <returns>True when the error indicates loss of the association; otherwise false.</returns>
        private static bool IsAssociationLost(Exception error) => error is GXDLMSException { ErrorCode: (int)ErrorCode.DisconnectMode } || error is GXDLMSConfirmedServiceError { ConfirmedServiceError: ConfirmedServiceError.InitiateError, ServiceError: ServiceError.Service, ServiceErrorValue: (byte)Service.Unsupported };
        /// <summary>Calculates the next receive or open timeout, capped by the active operation deadline.</summary>
        /// <returns>Positive remaining milliseconds, limited to WaitTime, or WaitTime when no deadline is active.</returns>
        /// <exception cref="TimeoutException">The active timeout or operation deadline has expired.</exception>
        private int RemainingWaitTime()
        {
            if (operationDeadline == 0)
            {
                return WaitTime;
            }

            long remainingTicks = operationDeadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                throw new TimeoutException(Resources.OperationDeadlineExceeded);
            }

            return (int)Math.Min(WaitTime, Math.Max(1, Math.Ceiling((double)remainingTicks * 1000 / Stopwatch.Frequency)));
        }

        /// <summary>Runs an action with an absolute deadline, preserving any earlier enclosing deadline and restoring it afterwards.</summary>
        /// <param name="action">Operation to execute while holding the asynchronous session gate.</param>
        /// <param name="timeout">Maximum duration of the scoped operation; an enclosing earlier deadline takes precedence.</param>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task WithDeadlineAsync(Func<Task> action, TimeSpan timeout)
        {
            long previous = operationDeadline;
            long ticks = (long)Math.Min(long.MaxValue - Stopwatch.GetTimestamp(), timeout.TotalSeconds * Stopwatch.Frequency);
            long deadline = Stopwatch.GetTimestamp() + ticks;
            operationDeadline = previous == 0 ? deadline : Math.Min(previous, deadline);
            try
            {
                await action().ConfigureAwait(false);
            }
            finally
            {
                operationDeadline = previous;
            }
        }

        /// <summary>Waits between frames or retries while observing cancellation and the active operation deadline.</summary>
        /// <param name="milliseconds">Requested delay in milliseconds before applying the current deadline.</param>
        /// <exception cref="OperationCanceledException">The current cancellation token is canceled.</exception>
        /// <exception cref="TimeoutException">The active timeout or operation deadline has expired.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task WaitAsync(int milliseconds)
        {
            if (operationDeadline != 0)
            {
                milliseconds = Math.Min(milliseconds, RemainingWaitTime());
            }

            if (milliseconds > 0)
            {
                await Task.Delay(milliseconds, operationToken).ConfigureAwait(false);
            }

            operationToken.ThrowIfCancellationRequested();
            if (operationDeadline != 0)
            {
                RemainingWaitTime();
            }
        }

        /// <summary>Delivers an observational report to every subscriber, isolating each subscriber's exception.</summary>
        /// <typeparam name="T">The report type accepted by the subscribers.</typeparam>
        /// <param name="callback">Optional single or multicast observer.</param>
        /// <param name="value">The report delivered synchronously to each subscriber.</param>
        /// <remarks>Observers cannot cancel an operation by throwing. The operation's cancellation token remains authoritative. No callback failure is sent to another observer, avoiding recursive diagnostic failures.</remarks>
        private static void InvokeObserver<T>(Action<T>? callback, T value)
        {
            if (callback == null)
            {
                return;
            }

            foreach (Action<T> observer in callback.GetInvocationList())
            {
                try
                {
                    observer(value);
                }
                catch (Exception)
                {
                    // Diagnostic and progress sinks must not control the protocol exchange.
                }
            }
        }

        /// <summary>Invokes the text trace callback when tracing is enabled, isolating observer exceptions.</summary>
        /// <param name="text">Text forwarded unchanged to the trace callback.</param>
        /// <param name="values">Additional values retained for call compatibility; they are not formatted into the text.</param>
        private void Log(string? text, params object?[] values)
        {
            if (Trace != TraceLevel.Off)
            {
                InvokeObserver(OnTrace, text);
            }
        }

        /// <summary>Converts a value to text and forwards it to the enabled trace callback.</summary>
        /// <param name="value">Value converted to text using the current culture.</param>
        private void Log(object? value) => Log(Convert.ToString(value));

        /// <summary>Closes the connection and detaches callbacks, permanently disabling this reader without disposing the caller-owned client or media.</summary>
        /// <remarks>Disposal waits for the session gate without cancellation. Repeated calls have no effect.</remarks>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
        /// <summary>Closes the connection asynchronously and detaches reader callbacks without disposing the caller-owned client or transport.</summary>
        /// <returns>An operation that completes after the connection is closed and callbacks are detached.</returns>
        /// <remarks>Repeated calls have no effect. Disposal waits for an active operation to finish.</remarks>
        public async ValueTask DisposeAsync()
        {
            await ExecuteAsync(async () =>
            {
                if (disposed)
                {
                    return;
                }

                try
                {
                    await CloseAsync().ConfigureAwait(false);
                }
                finally
                {
                    media.OnMediaStateChange -= MediaStateChanged;
                    initializeMedia = null;
                    initializeMediaAsync = null;
                    OnKeyPathRequested = null;
                    OnModeE = null;
                    OnNotification = null;
                    OnNotificationReceived = null;
                    OnTrace = null;
                    OnPacketTrace = null;
                    disposed = true;
                    GC.SuppressFinalize(this);
                }
            }, requireConnection: false, allowDisposed: true).ConfigureAwait(false);
        }
        /// <summary>Identifies a live serialized session scope flowing across asynchronous continuations.</summary>
        private sealed class SessionLease
        {
            /// <summary>Whether the owning operation still holds the session semaphore.</summary>
            public bool Active = true;
        }
    }
}
