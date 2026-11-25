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
using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Gurux.DLMS.DeviceAccess
{
    public partial class GXDLMSReader
    {

        /// <summary>Asynchronously receives buffered data until the requested timeout while observing cancellation and any operation deadline.</summary>
        /// <typeparam name="T">Transport receive payload type.</typeparam>
        /// <param name="parameters">Receive settings and destination for the received data.</param>
        /// <returns>A task returning true when the transport receives data before the timeout; otherwise false.</returns>
        /// <exception cref="OperationCanceledException">The current cancellation token is canceled.</exception>
        private async Task<bool> ReceiveAsync<T>(ReceiveParameters<T> parameters)
        {
            int timeout = parameters.WaitTime;
            var elapsed = Stopwatch.StartNew();
            try
            {
                do
                {
                    operationToken.ThrowIfCancellationRequested();
                    parameters.WaitTime = Math.Max(1, timeout - (int)elapsed.ElapsedMilliseconds);
                    if (operationDeadline != 0)
                    {
                        parameters.WaitTime = Math.Min(parameters.WaitTime, RemainingWaitTime());
                    }
                    if (await Media.ReceiveAsync(parameters, operationToken).ConfigureAwait(false))
                    {
                        operationToken.ThrowIfCancellationRequested();
                        return true;
                    }
                }
                while (elapsed.ElapsedMilliseconds < timeout);
                return false;
            }
            finally
            {
                parameters.WaitTime = timeout;
            }
        }
        /// <summary>Creates and exchanges custom requests inside the session gate without performing local access-rights checks.</summary>
        /// <param name="createMessages">Request factory invoked synchronously under the asynchronous session gate with the shared client.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <returns>The reply accumulator containing the final response and decoded value.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <exception cref="ArgumentNullException">The request factory is null.</exception>
        public GXReplyData Exchange(Func<GXDLMSClient, byte[][]> createMessages, CancellationToken cancellationToken = default) => ExchangeAsync(createMessages, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Creates and exchanges custom requests inside the session gate without performing local access-rights checks.</summary>
        /// <param name="createMessages">Request factory invoked synchronously under the asynchronous session gate with the shared client.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <returns>A task returning the reply accumulator containing the final response and decoded value.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <exception cref="ArgumentNullException">The request factory is null.</exception>
        public async Task<GXReplyData> ExchangeAsync(Func<GXDLMSClient, byte[][]> createMessages, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(createMessages);
            var reply = new GXReplyData();
            await ReadDataBlockAsync(createMessages(Client), reply).ConfigureAwait(false);
            return reply;
        }, cancellationToken).ConfigureAwait(false);
        /// <summary>Exchanges a raw request and parses a complete response frame without requiring an established association or checking local access rights.</summary>
        /// <param name="data">Caller-generated request frame data; null is used when receiving a streaming continuation.</param>
        /// <param name="reply">Reply accumulator populated by the exchange.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <exception cref="ArgumentNullException">The reply accumulator is null.</exception>
        public void ReadDLMSPacket(byte[]? data, GXReplyData reply, CancellationToken cancellationToken = default) => ReadDLMSPacketAsync(data, reply, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Exchanges a raw request and parses a complete response frame without requiring an established association or checking local access rights.</summary>
        /// <param name="data">Caller-generated request frame data; null is used when receiving a streaming continuation.</param>
        /// <param name="reply">Reply accumulator populated by the exchange.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <exception cref="ArgumentNullException">The reply accumulator is null.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ReadDLMSPacketAsync(byte[]? data, GXReplyData reply, CancellationToken cancellationToken = default) => await ExecuteAsync(async () => await ReadPacketCoreAsync(data, reply).ConfigureAwait(false), cancellationToken, requireConnection: false).ConfigureAwait(false);
        /// <summary>Sends a frame and assembles its response under the buffered receive scope, delivering notifications and retrying rejected replies.</summary>
        /// <param name="data">Caller-generated request frame data; null is used when receiving a streaming continuation.</param>
        /// <param name="reply">Reply accumulator populated by the exchange.</param>
        /// <remarks>A null request returns immediately unless the reply is streaming. Each retry has a fresh packet timeout, still bounded by the enclosing operation deadline.</remarks>
        /// <exception cref="ArgumentNullException">The reply accumulator is null.</exception>
        /// <exception cref="OperationCanceledException">The operation cancellation token is canceled.</exception>
        /// <exception cref="TimeoutException">A complete response is not received within the packet timeout or operation deadline.</exception>
        /// <exception cref="IOException">The transport is closed or the received frame exceeds 16 MiB.</exception>
        /// <exception cref="GXDLMSException">The meter returns a DLMS error after any rejection retries.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task ReadPacketCoreAsync(byte[]? data, GXReplyData reply) => await CompleteResponseAsync(async () => await ReadPacketResponseCoreAsync(data, reply).ConfigureAwait(false)).ConfigureAwait(false);

        /// <summary>Finishes an initiated request and all response blocks before observing caller cancellation; transport timeouts and deadlines remain active.</summary>
        /// <param name="action">Exchange to run without interrupting a pending meter response.</param>
        /// <returns>A task completed after the response is consumed.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before the request or after the complete response.</exception>
        private async Task CompleteResponseAsync(Func<Task> action)
        {
            CancellationToken pending = operationToken;
            pending.ThrowIfCancellationRequested();
            operationToken = CancellationToken.None;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception error) when (pending.IsCancellationRequested && (error is IOException || error is TimeoutException))
            {
                throw new OperationCanceledException(error.Message, error, pending);
            }
            finally
            {
                operationToken = pending;
            }
            pending.ThrowIfCancellationRequested();
        }

        /// <summary>Sends a request and consumes a complete response frame, retaining packet timeouts and operation deadlines.</summary>
        /// <param name="data">Request frame or null for a streaming continuation.</param>
        /// <param name="reply">Accumulator receiving the response.</param>
        /// <returns>A task completed when the frame has been parsed.</returns>
        private async Task ReadPacketResponseCoreAsync(byte[]? data, GXReplyData reply)
        {
            ArgumentNullException.ThrowIfNull(reply);
            operationToken.ThrowIfCancellationRequested();
            int packetTimeout = RemainingWaitTime();
            if (data == null && !reply.IsStreaming())
            {
                return;
            }

            if (!Media.IsOpen)
            {
                throw new IOException(Resources.MeterTransportClosed);
            }

            using (Media.BeginSynchronous())
            {
                for (int attempt = 0; ; ++attempt)
                {
                    if (data != null && data.Length != 0)
                    {
                        if (FrameDelay > 0 && lastSend != 0)
                        {
                            await WaitAsync(Math.Max(0, FrameDelay - (int)Stopwatch.GetElapsedTime(lastSend).TotalMilliseconds)).ConfigureAwait(false);
                        }

                        WriteTrace("TX: " + GXCommon.ToHex(data, true));
                        var packetObserver = OnPacketTrace;
                        if (packetObserver != null)
                        {
                            InvokeObserver(packetObserver, new GXPacketTrace(true, data));
                        }
                        await SendMediaAsync(data, null).ConfigureAwait(false);
                        lastSend = Stopwatch.GetTimestamp();
                    }

                    var received = new GXByteBuffer();
                    var trace = OnPacketTrace == null ? null : new GXByteBuffer();
                    var notify = new GXReplyData();
                    bool hdlc = Client.InterfaceType == InterfaceType.HDLC || Client.InterfaceType == InterfaceType.HdlcWithModeE || Client.InterfaceType == InterfaceType.PlcHdlc;
                    var parameters = new ReceiveParameters<byte[]>
                    {
                        Eop = hdlc ? (object)(byte)0x7E : null,
                        Count = Math.Max(1, Client.GetFrameSize(received)),
                        AllData = false
                    };
                    var elapsed = Stopwatch.StartNew();
                    reply.Error = 0;
                    while (true)
                    {
                        operationToken.ThrowIfCancellationRequested();
                        if (!Media.IsOpen)
                        {
                            throw new IOException(Resources.TransportClosedDuringResponse);
                        }

                        int remaining = Math.Min(RemainingWaitTime(), packetTimeout - (int)elapsed.ElapsedMilliseconds);
                        if (remaining <= 0)
                        {
                            throw new TimeoutException(Resources.MeterResponseTimeout);
                        }

                        parameters.WaitTime = remaining;
                        parameters.Reply = null;
                        if (!(await ReceiveAsync(parameters).ConfigureAwait(false)))
                        {
                            throw new TimeoutException(Resources.MeterResponseTimeout);
                        }

                        if (parameters.Reply == null || parameters.Reply.Length == 0)
                        {
                            continue;
                        }

                        received.Set(parameters.Reply);
                        trace?.Set(parameters.Reply);
                        if (received.Size > 16 * 1024 * 1024)
                        {
                            throw new IOException(Resources.FrameSizeExceeded);
                        }

                        // An optical head can echo the complete request. In particular, a DISC
                        // echo can be parsed as a reply when public client/server addresses match.
                        // Wait for the meter acknowledgement before starting the next association.
                        if (hdlc && data != null && data.Length != 0 && received.Size >= data.Length
                            && received.Data.AsSpan(0, data.Length).SequenceEqual(data))
                        {
                            received.Position = data.Length;
                            received.Trim();
                            if (received.Size == 0)
                            {
                                continue;
                            }
                        }

                        bool complete = Client.GetData(received, reply, notify);
                        if (notify.IsComplete && !notify.IsMoreData)
                        {
                            OnNotificationReceived?.Invoke(notify);
                            if (OnNotification != null)
                            {
                                if (notify.PrimeDc != null)
                                {
                                    OnNotification(notify.PrimeDc);
                                }
                                else
                                {
                                    new GXDLMSTranslator(TranslatorOutputType.SimpleXml).DataToXml(notify.Data, out string xml);
                                    OnNotification(xml);
                                }
                            }

                            notify.Clear();
                            received.Trim();
                        }

                        if (complete)
                        {
                            break;
                        }

                        if (!hdlc)
                        {
                            parameters.Count = Math.Max(1, Client.GetFrameSize(received));
                        }
                    }

                    WriteTrace("RX: " + received);
                    var responseObserver = OnPacketTrace;
                    if (trace != null && responseObserver != null)
                    {
                        InvokeObserver(responseObserver, new GXPacketTrace(false, trace.Array()));
                    }

                    if (reply.Error == (short)ErrorCode.Rejected && attempt + 1 < RetryCount)
                    {
                        reply.Clear();
                        await WaitAsync(100).ConfigureAwait(false);
                        continue;
                    }

                    if (reply.Error != 0)
                    {
                        throw new GXDLMSException(reply.Error);
                    }

                    return;
                }
            }
        }

        /// <summary>Exchanges raw request frames and follows all response blocks without checking association state or local access rights.</summary>
        /// <param name="data">Caller-generated request frame or frames; null sends no initial request.</param>
        /// <param name="reply">Reply accumulator populated by the exchange.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <returns>True when the final reply has no DLMS error, including when no request frames were supplied.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <remarks>Caller-generated frames must not be generated concurrently against the shared client because its invoke and block counters are mutable.</remarks>
        /// <exception cref="ArgumentNullException">The reply accumulator is null.</exception>
        public bool ReadDataBlock(byte[][]? data, GXReplyData reply, CancellationToken cancellationToken = default) => ReadDataBlockAsync(data, reply, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Exchanges raw request frames and follows all response blocks without checking association state or local access rights.</summary>
        /// <param name="data">Caller-generated request frame or frames; null sends no initial request.</param>
        /// <param name="reply">Reply accumulator populated by the exchange.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <returns>A task returning true when the final reply has no DLMS error, including when no request frames were supplied.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <remarks>Caller-generated frames must not be generated concurrently against the shared client because its invoke and block counters are mutable.</remarks>
        /// <exception cref="ArgumentNullException">The reply accumulator is null.</exception>
        public async Task<bool> ReadDataBlockAsync(byte[][]? data, GXReplyData reply, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(reply);
            if (data != null)
            {
                foreach (var message in data)
                {
                    reply.Clear();
                    await ReadDataBlockAsync(message, reply).ConfigureAwait(false);
                }
            }

            return reply.Error == 0;
        }, cancellationToken, requireConnection: false).ConfigureAwait(false);
        /// <summary>Exchanges raw request frames and follows all response blocks without checking association state or local access rights.</summary>
        /// <param name="data">Caller-generated request frame or frames; null sends no initial request.</param>
        /// <param name="reply">Reply accumulator populated by the exchange.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <remarks>Caller-generated frames must not be generated concurrently against the shared client because its invoke and block counters are mutable.</remarks>
        /// <exception cref="ArgumentNullException">The reply accumulator is null.</exception>
        public void ReadDataBlock(byte[]? data, GXReplyData reply, CancellationToken cancellationToken = default) => ReadDataBlockAsync(data, reply, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Exchanges raw request frames and follows all response blocks without checking association state or local access rights.</summary>
        /// <param name="data">Caller-generated request frame or frames; null sends no initial request.</param>
        /// <param name="reply">Reply accumulator populated by the exchange.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session gate and at complete response boundaries.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Caller cancellation is observed before a request or after its complete response, including continuation blocks. Packet timeouts and operation deadlines remain active. Cancellation, timeout or I/O failure invalidates the association and closes the transport. Request factories and synchronous callbacks execute while the session gate is held; asynchronous initialization callbacks are awaited under the same gate. No UI synchronization context is guaranteed.</remarks>
        /// <remarks>Caller-generated frames must not be generated concurrently against the shared client because its invoke and block counters are mutable.</remarks>
        /// <exception cref="ArgumentNullException">The reply accumulator is null.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ReadDataBlockAsync(byte[]? data, GXReplyData reply, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            await CompleteResponseAsync(async () =>
            {
                using (Media.BeginSynchronous())
                {
                    await ReadPacketCoreAsync(data, reply).ConfigureAwait(false);
                    while (reply.IsMoreData)
                    {
                        operationToken.ThrowIfCancellationRequested();
                        await ReadPacketCoreAsync(reply.IsStreaming() ? null : Client.ReceiverReady(reply), reply).ConfigureAwait(false);
                    }
                }
            }).ConfigureAwait(false);
        }, cancellationToken, requireConnection: false).ConfigureAwait(false);

        /// <summary>Encodes an optical command or sends an existing binary frame through the asynchronous transport.</summary>
        /// <param name="data">Binary frame or UTF-8 optical command.</param>
        /// <param name="receiver">Transport destination, or null for the configured destination.</param>
        /// <returns>A task completed after the command has been transmitted.</returns>
        /// <exception cref="OperationCanceledException">The caller cancels the transmission.</exception>
        /// <exception cref="TimeoutException">Transmission exceeds the active packet timeout or operation deadline.</exception>
        private async Task SendMediaAsync(object data, string? receiver)
        {
            byte[] bytes = data as byte[] ?? Encoding.UTF8.GetBytes(Convert.ToString(data, CultureInfo.InvariantCulture) ?? string.Empty);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
            timeout.CancelAfter(RemainingWaitTime());
            try
            {
                await Media.SendAsync(bytes, receiver, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!operationToken.IsCancellationRequested)
            {
                throw new TimeoutException(Resources.MeterResponseTimeout);
            }
        }

    }
}
