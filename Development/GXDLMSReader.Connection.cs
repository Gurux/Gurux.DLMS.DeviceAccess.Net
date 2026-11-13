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
using Gurux.DLMS.Objects;
using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Gurux.DLMS.DeviceAccess
{
    public partial class GXDLMSReader
    {

        /// <summary>
        /// Negotiates HDLC link parameters and parses the UA reply when the interface requires SNRM.
        /// </summary>
        public void SNRMRequest() => SNRMRequestAsync().GetAwaiter().GetResult();
        /// <summary>
        /// Negotiates HDLC link parameters and parses the UA reply when the interface requires SNRM.
        /// </summary>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task SNRMRequestAsync()
        {
            GXReplyData reply = new GXReplyData();
            byte[] data;
            data = Client.SNRMRequest();
            if (data != null)
            {
                if (Trace > TraceLevel.Info)
                {
                    Log(string.Format(CultureInfo.CurrentCulture, Resources.SendingSnrmRequest, GXCommon.ToHex(data, true)));
                }

                await ReadDataBlockAsync(data, reply).ConfigureAwait(false);
                if (Trace == TraceLevel.Verbose)
                {
                    Log(string.Format(CultureInfo.CurrentCulture, Resources.ParsingUaReply, reply));
                }

                //Has server accepted client.
                Client.ParseUAResponse(reply.Data);
                if (Trace > TraceLevel.Info)
                {
                    Log(Resources.UaReplyParsed);
                }
            }
        }

        /// <summary>
        /// Sends the application association request, parses its response, and performs high authentication when required.
        /// </summary>
        public void AarqRequest() => AarqRequestAsync().GetAwaiter().GetResult();
        /// <summary>
        /// Sends the application association request, parses its response, and performs high authentication when required.
        /// </summary>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task AarqRequestAsync()
        {
            GXReplyData reply = new GXReplyData();
            //Generate AARQ request.
            //Split requests to multiple packets if needed.
            //If password is used all data might not fit to one packet.
            var aarq = Client.AARQRequest();
            //AARQ is not used for pre-established connections.
            if (aarq.Length != 0)
            {
                foreach (byte[] it in aarq)
                {
                    if (Trace > TraceLevel.Info)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.SendingAarqRequest, GXCommon.ToHex(it, true)));
                    }

                    reply.Clear();
                    await ReadDataBlockAsync(it, reply).ConfigureAwait(false);
                }

                if (Trace > TraceLevel.Info)
                {
                    Log(string.Format(CultureInfo.CurrentCulture, Resources.ParsingAareReply, reply));
                }

                //Parse reply.
                Client.ParseAAREResponse(reply.Data);
                reply.Clear();
                //Get challenge Is HLS authentication is used.
                if (Client.Authentication > Authentication.Low)
                {
                    foreach (byte[] it in Client.GetApplicationAssociationRequest())
                    {
                        reply.Clear();
                        await ReadDataBlockAsync(it, reply).ConfigureAwait(false);
                    }

                    Client.ParseApplicationAssociationResponse(reply.Data);
                }

                if (Trace > TraceLevel.Info)
                {
                    Log(Resources.AareReplyParsed);
                }
            }
        }

        /// <summary>
        /// Reads the configured invocation counter through an unsecured public association and sets the next ciphering counter.
        /// </summary>
        /// <remarks>Runs only when counter synchronization and ciphering are enabled. Restores the original client security and addressing settings after the attempt.</remarks>
        /// <exception cref="OverflowException">The meter counter cannot be incremented within the unsigned 32-bit range.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task UpdateFrameCounterAsync()
        {
            //Read frame counter if GeneralProtection is used.
            if (!string.IsNullOrEmpty(connectOptions.InvocationCounter) && Client.Ciphering != null && Client.Ciphering.Security != Security.None)
            {
                //Media settings are saved and they are restored when HDLC with mode E is used.
                string? mediaSettings = Media.Settings;
                await PrepareMediaAsync().ConfigureAwait(false);
                byte[] data;
                GXReplyData reply = new GXReplyData();
                int add = Client.ClientAddress;
                int serverAddress = Client.ServerAddress;
                Authentication auth = Client.Authentication;
                Security security = Client.Ciphering.Security;
                Signing signing = Client.Ciphering.Signing;
                byte[] challenge = Client.CtoSChallenge;
                byte[] serverSystemTitle = Client.ServerSystemTitle;
                try
                {
                    Client.ServerSystemTitle = null;
                    Client.ClientAddress = 16;
                    Client.Authentication = Authentication.None;
                    Client.Ciphering.Security = Security.None;
                    Client.Ciphering.Signing = Signing.None;
                    data = Client.SNRMRequest();
                    if (data != null)
                    {
                        if (Trace > TraceLevel.Info)
                        {
                            Log(string.Format(CultureInfo.CurrentCulture, Resources.SendingSnrmRequest, GXCommon.ToHex(data, true)));
                        }

                        await ReadDataBlockAsync(data, reply).ConfigureAwait(false);
                        if (Trace == TraceLevel.Verbose)
                        {
                            Log(string.Format(CultureInfo.CurrentCulture, Resources.ParsingUaReply, reply));
                        }

                        //Has server accepted client.
                        Client.ParseUAResponse(reply.Data);
                        if (Trace > TraceLevel.Info)
                        {
                            Log(Resources.UaReplyParsed);
                        }
                    }

                    //Generate AARQ request.
                    //Split requests to multiple packets if needed.
                    //If password is used all data might not fit to one packet.
                    foreach (byte[] it in Client.AARQRequest())
                    {
                        if (Trace > TraceLevel.Info)
                        {
                            Log(string.Format(CultureInfo.CurrentCulture, Resources.SendingAarqRequest, GXCommon.ToHex(it, true)));
                        }

                        reply.Clear();
                        await ReadDataBlockAsync(it, reply).ConfigureAwait(false);
                    }

                    if (Trace > TraceLevel.Info)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.ParsingAareReply, reply));
                    }

                    try
                    {
                        //Parse reply.
                        Client.ParseAAREResponse(reply.Data);
                        reply.Clear();
                        GXDLMSData d = new GXDLMSData(connectOptions.InvocationCounter);
                        var counterReply = new GXReplyData();
                        await ReadDataBlockAsync(Client.Read(d, 2), counterReply).ConfigureAwait(false);
                        Client.UpdateValue(d, 2, counterReply.Value);
                        Client.Ciphering.InvocationCounter = checked(1 + Convert.ToUInt32(d.Value));
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.InvocationCounterValue, Client.Ciphering.InvocationCounter));
                        reply.Clear();
                        await DisconnectAsync().ConfigureAwait(false);
                        //Reset media settings back to default.
                        if (Client.InterfaceType == InterfaceType.HdlcWithModeE)
                        {
                            await Media.CloseAsync().ConfigureAwait(false);
                            Media.Settings = mediaSettings;
                        }
                    }
                    catch (Exception)
                    {
                        await DisconnectAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                finally
                {
                    Client.ServerSystemTitle = serverSystemTitle;
                    Client.ClientAddress = add;
                    Client.ServerAddress = serverAddress;
                    Client.Authentication = auth;
                    Client.Ciphering.Security = security;
                    Client.CtoSChallenge = challenge;
                    Client.Ciphering.Signing = signing;
                    if (Client.PreEstablishedConnection)
                    {
                        Client.NegotiatedConformance |= Conformance.GeneralProtection;
                    }
                }
            }
        }

        /// <summary>Delegates the complete IEC 62056-21 disconnect exchange to the host.</summary>
        /// <exception cref="InvalidOperationException">The Mode E host callback is not configured.</exception>
        /// <exception cref="OperationCanceledException">The active operation is canceled.</exception>
        /// <returns>A task completed after the host has sent the disconnect command and consumed its response.</returns>
        private Task DiscIECAsync() => NotifyModeEAsync(ModeEOperation.DisconnectModeE, 0);

        /// <summary>Awaits a host-owned optical transport operation using the active session cancellation token.</summary>
        /// <param name="operation">The optical transport operation requested from the host.</param>
        /// <param name="baudRate">Requested baud rate, or zero when the operation does not change it.</param>
        /// <exception cref="InvalidOperationException">The Mode E host callback is not configured.</exception>
        /// <exception cref="OperationCanceledException">The active operation is canceled.</exception>
        /// <returns>A task completed after the host operation finishes.</returns>
        private async Task NotifyModeEAsync(ModeEOperation operation, int baudRate)
        {
            operationToken.ThrowIfCancellationRequested();
            var callback = OnModeE;
            if (callback == null)
            {
                throw new InvalidOperationException(Resources.ModeECallbackRequired);
            }

            await callback(media, operation, baudRate, operationToken).ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();
        }
        /// <summary>
        /// Performs IEC 62056-21 identification and mode E negotiation for an HDLC optical connection.
        /// </summary>
        /// <remarks>Uses the saved connection options for optical delays and receive timeouts. The host callback switches the media to the negotiated baud rate and 8N1 framing. Other interface types require no optical handshake.</remarks>
        /// <exception cref="Exception">The identification reply or baud-rate code is invalid.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        async Task InitializeOpticalHeadAsync()
        {
            if (Client.InterfaceType != InterfaceType.HdlcWithModeE)
            {
                return;
            }

            if (OnModeE == null)
            {
                throw new InvalidOperationException(Resources.ModeECallbackRequired);
            }

            using var opticalScope = Media.BeginSynchronous();
            byte Terminator = (byte)0x0A;
            if (!Media.IsOpen)
            {
                await OpenMediaAsync().ConfigureAwait(false);
            }

            await WaitAsync(connectOptions.OpticalHeadInitialDelay).ConfigureAwait(false);
            //Query device information.
            string data = "/?!\r\n";
            if (Trace > TraceLevel.Info)
            {
                Log(string.Format(CultureInfo.CurrentCulture, Resources.SendingIecData, data));
            }

            ReceiveParameters<string> p = new ReceiveParameters<string>()
            {
                AllData = false,
                Eop = Terminator,
                WaitTime = WaitTime
            };
            using (Media.BeginSynchronous())
            {
                await SendMediaAsync(data, null).ConfigureAwait(false);
                if (!(await ReceiveAsync(p).ConfigureAwait(false)))
                {
                    //Try to move away from mode E.
                    try
                    {
                        await DisconnectAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }

                    await DiscIECAsync().ConfigureAwait(false);
                    string str = Resources.DeviceReplyTimeout;
                    if (Trace > TraceLevel.Info)
                    {
                        Log(str);
                    }

                    await SendMediaAsync(data, null).ConfigureAwait(false);
                    if (!(await ReceiveAsync(p).ConfigureAwait(false)))
                    {
                        throw new Exception(str);
                    }
                }

                //If echo is used.
                if (p.Reply == data)
                {
                    p.Reply = null;
                    if (!(await ReceiveAsync(p).ConfigureAwait(false)))
                    {
                        //Try to move away from mode E.
                        GXReplyData reply = new GXReplyData();
                        await DisconnectAsync().ConfigureAwait(false);
                        await DiscIECAsync().ConfigureAwait(false);
                        await NotifyModeEAsync(ModeEOperation.RecoverModeE, 9600).ConfigureAwait(false);
                        await DiscIECAsync().ConfigureAwait(false);

                        data = Resources.DeviceReplyTimeout;
                        if (Trace > TraceLevel.Info)
                        {
                            Log(data);
                        }

                        throw new Exception(data);
                    }
                }
            }

            if (Trace > TraceLevel.Info)
            {
                Log(string.Format(CultureInfo.CurrentCulture, Resources.ReceivedIecData, p.Reply));
            }

            string identification = p.Reply ?? string.Empty;
            int pos = 0;
            //With some meters there might be some extra invalid chars. Remove them.
            while (pos < identification.Length && identification[pos] != '/')
            {
                ++pos;
            }

            if (pos >= identification.Length || identification.Length - pos < 5 || identification[pos] != '/')
            {
                p.WaitTime = connectOptions.OpticalHeadInvalidResponseWaitTime;
                await ReceiveAsync(p).ConfigureAwait(false);
                await DiscIECAsync().ConfigureAwait(false);
                throw new Exception(Resources.InvalidOpticalResponse);
            }

            string manufactureID = identification.Substring(1 + pos, 3);
            char baudrate = identification[4 + pos];
            int BaudRate = 0;
            switch (baudrate)
            {
                case '0':
                    BaudRate = 300;
                    break;
                case '1':
                    BaudRate = 600;
                    break;
                case '2':
                    BaudRate = 1200;
                    break;
                case '3':
                    BaudRate = 2400;
                    break;
                case '4':
                    BaudRate = 4800;
                    break;
                case '5':
                    BaudRate = 9600;
                    break;
                case '6':
                    BaudRate = 19200;
                    break;
                case '7':
                    BaudRate = 38400;
                    break;
                case '8':
                    BaudRate = 57600;
                    break;
                case '9':
                    BaudRate = 115200;
                    break;
                default:
                    throw new Exception(Resources.UnknownBaudRate);
            }

            if (Trace > TraceLevel.Info)
            {
                Log(string.Format(CultureInfo.CurrentCulture, Resources.OpticalBaudRate, DateTime.Now, BaudRate));
            }

            //Send ACK
            //Send Protocol control character
            // "2" HDLC protocol procedure (Mode E)
            byte controlCharacter = (byte)'2';
            //Send Baud rate character
            //Mode control character
            byte ModeControlCharacter = (byte)'2';
            //"2" //(HDLC protocol procedure) (Binary mode)
            //Set mode E.
            byte[] arr = new byte[]
            {
                0x06,
                controlCharacter,
                (byte)baudrate,
                ModeControlCharacter,
                13,
                10
            };
            if (Trace > TraceLevel.Info)
            {
                Log(string.Format(CultureInfo.CurrentCulture, Resources.SwitchingToModeE, DateTime.Now));
            }

            using (Media.BeginSynchronous())
            {
                p.Reply = null;
                await SendMediaAsync(arr, null).ConfigureAwait(false);
                await WaitAsync(connectOptions.OpticalHeadSwitchDelay).ConfigureAwait(false);
                p.WaitTime = connectOptions.OpticalHeadEchoWaitTime;
                await ReceiveAsync(p).ConfigureAwait(false);
                if (p.Reply != null)
                {
                    if (Trace > TraceLevel.Info)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.ReceivedData, p.Reply));
                    }
                }

                await NotifyModeEAsync(ModeEOperation.SwitchModeE, BaudRate).ConfigureAwait(false);


                await WaitAsync(connectOptions.OpticalHeadReadyDelay).ConfigureAwait(false);
            }
        }
        /// <summary>Opens the transport if necessary and runs the custom initializer or optical-head initialization.</summary>
        /// <exception cref="OperationCanceledException">The operation cancellation token is canceled.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task PrepareMediaAsync()
        {
            operationToken.ThrowIfCancellationRequested();
            if (!Media.IsOpen)
            {
                await OpenMediaAsync().ConfigureAwait(false);
            }

            if (initializeMediaAsync != null)
            {
                await initializeMediaAsync(operationToken).ConfigureAwait(false);
                operationToken.ThrowIfCancellationRequested();
            }
            else if (initializeMedia != null)
            {
                initializeMedia(operationToken);
            }
            else
            {
                await InitializeOpticalHeadAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Asynchronously opens the transport using a linked cancellation token and the remaining timeout.</summary>
        /// <exception cref="TimeoutException">The active timeout or operation deadline has expired.</exception>
        /// <remarks>Transport opening and I/O are awaited with cancellation and timeouts. Synchronous entry points block until their asynchronous implementation completes.</remarks>
        /// <exception cref="OperationCanceledException">The operation cancellation token is canceled.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task OpenMediaAsync()
        {
            operationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
            timeout.CancelAfter(RemainingWaitTime());
            try
            {
                await Media.OpenAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!operationToken.IsCancellationRequested)
            {
                throw new TimeoutException(Resources.TransportOpenTimeout);
            }
        }

        /// <summary>Opens the transport, initializes and authenticates the association, and optionally loads a missing association view.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A connection receive timeout or retry count is not positive, or an optical delay is negative.</exception>
        /// <param name="options">Connection and discovery settings, or null to reuse the previous handshake settings and load a missing association view.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Transport opening and I/O are awaited with cancellation and timeouts. Synchronous entry points block until their asynchronous implementation completes.</remarks>
        public void Connect(GXDLMSConnectOptions? options = null, CancellationToken cancellationToken = default) => ConnectAsync(options, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Opens the transport, initializes and authenticates the association, and optionally loads a missing association view.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A connection receive timeout or retry count is not positive, or an optical delay is negative.</exception>
        /// <param name="options">Connection and discovery settings, or null to reuse the previous handshake settings and load a missing association view.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Transport opening and I/O are awaited with cancellation and timeouts. Synchronous entry points block until their asynchronous implementation completes.</remarks>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ConnectAsync(GXDLMSConnectOptions? options = null, CancellationToken cancellationToken = default) => await ConnectCoreAsync(options, options?.ReadAssociationView ?? true, cancellationToken).ConfigureAwait(false);
        /// <summary>Serializes connection setup and retains copied handshake settings for subsequent internal reconnects.</summary>
        /// <param name="options">Handshake settings to copy, or null to preserve the previous settings.</param>
        /// <param name="readAssociationView">Whether to load a missing association view.</param>
        /// <param name="cancellationToken">Cancellation observed while acquiring the session and during communication.</param>
        /// <exception cref="OperationCanceledException">Connection setup is canceled.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or communication exceeds its timeout.</exception>
        /// <exception cref="IOException">Transport opening or communication fails.</exception>
        /// <exception cref="GXDLMSException">The meter rejects connection setup or discovery.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A connection receive timeout or retry count is not positive, or an optical delay is negative.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task ConnectCoreAsync(GXDLMSConnectOptions? options, bool readAssociationView, CancellationToken cancellationToken) => await ExecuteAsync(async () =>
        {
            if (options != null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.WaitTime);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RetryCount);
                ArgumentOutOfRangeException.ThrowIfNegative(options.OpticalHeadInitialDelay);
                ArgumentOutOfRangeException.ThrowIfNegative(options.OpticalHeadSwitchDelay);
                ArgumentOutOfRangeException.ThrowIfNegative(options.OpticalHeadReadyDelay);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OpticalHeadEchoWaitTime);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OpticalHeadInvalidResponseWaitTime);
                connectOptions = new GXDLMSConnectOptions
                {
                    WaitTime = options.WaitTime,
                    RetryCount = options.RetryCount,
                    OpticalHeadInitialDelay = options.OpticalHeadInitialDelay,
                    OpticalHeadSwitchDelay = options.OpticalHeadSwitchDelay,
                    OpticalHeadReadyDelay = options.OpticalHeadReadyDelay,
                    OpticalHeadEchoWaitTime = options.OpticalHeadEchoWaitTime,
                    OpticalHeadInvalidResponseWaitTime = options.OpticalHeadInvalidResponseWaitTime,
                    InvocationCounter = options.InvocationCounter,
                    IgnoreSNRMWithPreEstablished = options.IgnoreSNRMWithPreEstablished,
                    ReadAssociationView = options.ReadAssociationView
                };
            }

            if (!IsConnected)
            {
                bool previousSetupActive = connectionSetupActive;
                connectionSetupActive = true;
                try
                {
                    if (!Media.IsOpen)
                    {
                        if (Client.InterfaceType == InterfaceType.HdlcWithModeE && initializeMedia == null && initializeMediaAsync == null)
                        {
                            await NotifyModeEAsync(ModeEOperation.InitModeE, 300).ConfigureAwait(false);
                        }

                        await OpenMediaAsync().ConfigureAwait(false);
                    }

                    await UpdateFrameCounterAsync().ConfigureAwait(false);
                    await PrepareMediaAsync().ConfigureAwait(false);
                    if (!(Client.PreEstablishedConnection && IgnoreSNRMWithPreEstablished))
                    {
                        await SNRMRequestAsync().ConfigureAwait(false);
                    }

                    if (!Client.PreEstablishedConnection)
                    {
                        await AarqRequestAsync().ConfigureAwait(false);
                    }

                    connected = true;
                }
                catch
                {
                    connected = false;
                    try
                    {
                        await Media.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    throw;
                }
                finally
                {
                    connectionSetupActive = previousSetupActive;
                }
            }

            if (readAssociationView)
            {
                await EnsureAssociationViewAsync().ConfigureAwait(false);
            }
        }, cancellationToken, false).ConfigureAwait(false);
        /// <summary>Establishes the connection using connection-specific timeouts and retries without loading the association view.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A connection receive timeout or retry count is not positive, or an optical delay is negative.</exception>
        /// <param name="options">Connection settings to copy, or null to reuse the previous settings, initially the connection defaults. ReadAssociationView is ignored.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        public void InitializeConnection(GXDLMSConnectOptions? options = null, CancellationToken cancellationToken = default) => InitializeConnectionAsync(options, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Establishes the connection using connection-specific timeouts and retries without loading the association view.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A connection receive timeout or retry count is not positive, or an optical delay is negative.</exception>
        /// <param name="options">Connection settings to copy, or null to reuse the previous settings, initially the connection defaults. ReadAssociationView is ignored.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task InitializeConnectionAsync(GXDLMSConnectOptions? options = null, CancellationToken cancellationToken = default) => await ConnectCoreAsync(options, false, cancellationToken).ConfigureAwait(false);
        /// <summary>Retrieves the association view when missing or explicitly refreshed and returns the client object collection.</summary>
        /// <param name="forceRefresh">Whether to retrieve the view again even when it is already loaded; also bypasses the XML cache when applicable.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>The shared client object collection containing the loaded association view.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>For versioned short-name associations, access rights are read after parsing the view without a local rights check.</remarks>
        public GXDLMSObjectCollection EnsureAssociationView(bool forceRefresh = false, CancellationToken cancellationToken = default) => EnsureAssociationViewAsync(forceRefresh, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Retrieves the association view when missing or explicitly refreshed and returns the client object collection.</summary>
        /// <param name="forceRefresh">Whether to retrieve the view again even when it is already loaded; also bypasses the XML cache when applicable.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning the shared client object collection containing the loaded association view.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>For versioned short-name associations, access rights are read after parsing the view without a local rights check.</remarks>
        public async Task<GXDLMSObjectCollection> EnsureAssociationViewAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            if (!AssociationViewLoaded || forceRefresh)
            {
                var reply = new GXReplyData();
                await ReadDataBlockAsync(Client.GetObjectsRequest(), reply).ConfigureAwait(false);
                Client.ParseObjects(reply.Data, true);
                if (!Client.UseLogicalNameReferencing)
                {
                    var sn = Client.Objects.FindBySN(0xFA00) as GXDLMSAssociationShortName;
                    if (sn != null && sn.Version > 0)
                    {
                        await ReadAsync(sn, 3, false).ConfigureAwait(false);
                    }
                }

                AssociationViewLoaded = true;
            }

            return Client.Objects;
        }, cancellationToken).ConfigureAwait(false);
        /// <summary>Loads an optional caller-owned XML cache or retrieves the association view from the meter and optionally saves it.</summary>
        /// <param name="outputFile">Optional caller-owned XML cache path to load, or to save after retrieval from the meter.</param>
        /// <param name="forceRefresh">Whether to retrieve the view again even when it is already loaded; also bypasses the XML cache when applicable.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>True if the view was retrieved from the meter; false if already loaded or restored from the XML cache.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Unreadable or malformed cache files are logged and replaced by a meter retrieval. Errors while saving a refreshed cache propagate to the caller.</remarks>
        public bool GetAssociationView(string? outputFile = null, bool forceRefresh = false, CancellationToken cancellationToken = default) => GetAssociationViewAsync(outputFile, forceRefresh, cancellationToken).GetAwaiter().GetResult();
        /// <summary>Loads an optional caller-owned XML cache or retrieves the association view from the meter and optionally saves it.</summary>
        /// <param name="outputFile">Optional caller-owned XML cache path to load, or to save after retrieval from the meter.</param>
        /// <param name="forceRefresh">Whether to retrieve the view again even when it is already loaded; also bypasses the XML cache when applicable.</param>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <returns>A task returning true if the view was retrieved from the meter; false if already loaded or restored from the XML cache.</returns>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="InvalidOperationException">No usable connection has been established.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Unreadable or malformed cache files are logged and replaced by a meter retrieval. Errors while saving a refreshed cache propagate to the caller.</remarks>
        public async Task<bool> GetAssociationViewAsync(string? outputFile = null, bool forceRefresh = false, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            if (AssociationViewLoaded && !forceRefresh)
            {
                return false;
            }

            if (!forceRefresh && !string.IsNullOrWhiteSpace(outputFile) && File.Exists(outputFile))
            {
                try
                {
                    var objects = GXDLMSObjectCollection.Load(outputFile);
                    Client.Objects.Clear();
                    Client.Objects.AddRange(objects);
                    AssociationViewLoaded = true;
                    return false;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    Log(string.Format(CultureInfo.CurrentCulture, Resources.AssociationCacheLoadFailed, error.Message));
                }
            }

            await EnsureAssociationViewAsync(true).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(outputFile))
            {
                Client.Objects.Save(outputFile, new GXXmlWriterSettings { Values = false });
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
        /// <summary>Sends a release request for an open, non-pre-established association using wrapper framing or security, leaving the transport open.</summary>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        public void Release(CancellationToken cancellationToken = default) => ReleaseAsync(cancellationToken).GetAwaiter().GetResult();
        /// <summary>Sends a release request for an open, non-pre-established association using wrapper framing or security, leaving the transport open.</summary>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ReleaseAsync(CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            if (Media.IsOpen && !Client.PreEstablishedConnection && (Client.InterfaceType == InterfaceType.WRAPPER || Client.Ciphering.Security != Security.None))
            {
                await ReadDataBlockAsync(Client.ReleaseRequest(), new GXReplyData()).ConfigureAwait(false);
                connected = false;
            }
        }, cancellationToken, false).ConfigureAwait(false);
        /// <summary>Releases and disconnects a non-pre-established association when the transport is open, leaving the transport open.</summary>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        public void Disconnect(CancellationToken cancellationToken = default) => DisconnectAsync(cancellationToken).GetAwaiter().GetResult();
        /// <summary>Releases and disconnects a non-pre-established association when the transport is open, leaving the transport open.</summary>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="ObjectDisposedException">The reader has been disposed.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default) => await ExecuteAsync(async () => await DisconnectCoreAsync().ConfigureAwait(false), cancellationToken, false).ConfigureAwait(false);
        /// <summary>Attempts release and disconnection and always marks the association unusable, tolerating a DLMS release rejection.</summary>
        /// <returns>A task representing completion of the operation.</returns>
        private async Task DisconnectCoreAsync()
        {
            try
            {
                if (Media.IsOpen && !Client.PreEstablishedConnection)
                {
                    try
                    {
                        await ReleaseAsync().ConfigureAwait(false);
                    }
                    catch (GXDLMSException error)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.ReleaseRejected, error.Message));
                    }

                    var request = Client.DisconnectRequest();
                    if (request != null && request.Length != 0)
                    {
                        await ReadDataBlockAsync(request, new GXReplyData()).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                connected = false;
            }
        }

        /// <summary>Disconnects an active association and closes the caller-owned transport. The client and media remain available for reconnection.</summary>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Already disposed readers are ignored. Once the session gate is acquired, the transport is closed even if protocol disconnection fails.</remarks>
        public void Close(CancellationToken cancellationToken = default) => CloseAsync(cancellationToken).GetAwaiter().GetResult();
        /// <summary>Disconnects an active association and closes the caller-owned transport. The client and media remain available for reconnection.</summary>
        /// <param name="cancellationToken">Cancellation token observed while acquiring the asynchronous session gate and during supported I/O waits.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <exception cref="TimeoutException">Opening or exchanging data exceeds its timeout or active deadline.</exception>
        /// <exception cref="System.IO.IOException">The transport is closed or fails during the exchange.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a DLMS request.</exception>
        /// <remarks>Already disposed readers are ignored. Once the session gate is acquired, the transport is closed even if protocol disconnection fails.</remarks>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            if (disposed)
            {
                return;
            }

            await ExecuteAsync(async () =>
            {
                if (disposed)
                {
                    return;
                }

                try
                {
                    if (IsConnected)
                    {
                        await DisconnectCoreAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    connected = false;
                    await media.CloseAsync().ConfigureAwait(false);
                }
            }, cancellationToken, false, allowDisposed: true).ConfigureAwait(false);
        }

    }
}
