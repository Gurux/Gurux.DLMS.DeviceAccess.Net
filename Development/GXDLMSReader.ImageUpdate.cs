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

using Gurux.DLMS.Enums;
using Gurux.DLMS.Objects;
using Gurux.DLMS.Objects.Enums;
using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Linq;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace Gurux.DLMS.DeviceAccess
{
    public partial class GXDLMSReader
    {
        /// <summary>
        /// Transfers an image and optionally verifies and activates it, reporting success only for completed requested stages.
        /// </summary>
        /// <param name="target">Meter image-transfer object to operate on.</param>
        /// <param name="identification">Nonempty image identifier used for initiation and activation matching.</param>
        /// <param name="image">Nonempty firmware bytes to divide into meter-sized blocks.</param>
        /// <param name="options">Transfer, retry, polling, and progress settings, or null to use defaults.</param>
        /// <param name="checkAccessRights">Whether to enforce cached access rights, or null to use the reader default. False bypasses local checks only.</param>
        /// <param name="cancellationToken">Token that cancels communication, block transfer, and status polling.</param>
        /// <remarks>Resume reuses only an initiated transfer with a matching image identifier and size. Missing blocks are retransmitted according to the confirmed block bitmap. Verification and activation each have their own deadline; activation reconnects only when configured. Progress callbacks run synchronously on the executing thread. Completed means all requested stages succeeded; activation is complete only after the meter reports ActivationSuccessful.</remarks>
        /// <exception cref="ArgumentNullException">The target, identifier, or image is null.</exception>
        /// <exception cref="ArgumentException">The identifier or image is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An option value is outside its supported range.</exception>
        /// <exception cref="InvalidOperationException">Transfer is disabled, the block size is invalid, verification or activation fails, or the verified image does not match.</exception>
        /// <exception cref="IOException">Blocks remain unconfirmed after retries, or verification loses its connection.</exception>
        /// <exception cref="TimeoutException">Communication or a verification or activation deadline expires.</exception>
        /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a protocol operation.</exception>
        /// <exception cref="UnauthorizedAccessException">Cached association rights deny a required operation and local checks are enabled.</exception>
        public void ImageUpdate(GXDLMSImageTransfer target, byte[] identification, byte[] image, GXImageUpdateOptions? options = null, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => ImageUpdateAsync(target, identification, image, options, checkAccessRights, cancellationToken).GetAwaiter().GetResult();
        /// <summary>
        /// Transfers an image and optionally verifies and activates it, reporting success only for completed requested stages.
        /// </summary>
        /// <param name="target">Meter image-transfer object to operate on.</param>
        /// <param name="identification">Nonempty image identifier used for initiation and activation matching.</param>
        /// <param name="image">Nonempty firmware bytes to divide into meter-sized blocks.</param>
        /// <param name="options">Transfer, retry, polling, and progress settings, or null to use defaults.</param>
        /// <param name="checkAccessRights">Whether to enforce cached access rights, or null to use the reader default. False bypasses local checks only.</param>
        /// <param name="cancellationToken">Token that cancels communication, block transfer, and status polling.</param>
        /// <remarks>Resume reuses only an initiated transfer with a matching image identifier and size. Missing blocks are retransmitted according to the confirmed block bitmap. Verification and activation each have their own deadline; activation reconnects only when configured. Progress callbacks run synchronously on the executing thread. Completed means all requested stages succeeded; activation is complete only after the meter reports ActivationSuccessful.</remarks>
        /// <exception cref="ArgumentNullException">The target, identifier, or image is null.</exception>
        /// <exception cref="ArgumentException">The identifier or image is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An option value is outside its supported range.</exception>
        /// <exception cref="InvalidOperationException">Transfer is disabled, the block size is invalid, verification or activation fails, or the verified image does not match.</exception>
        /// <exception cref="IOException">Blocks remain unconfirmed after retries, or verification loses its connection.</exception>
        /// <exception cref="TimeoutException">Communication or a verification or activation deadline expires.</exception>
        /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a protocol operation.</exception>
        /// <exception cref="UnauthorizedAccessException">Cached association rights deny a required operation and local checks are enabled.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ImageUpdateAsync(GXDLMSImageTransfer target, byte[] identification, byte[] image, GXImageUpdateOptions? options = null, bool? checkAccessRights = null, CancellationToken cancellationToken = default) => await ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(identification);
            ArgumentNullException.ThrowIfNull(image);
            if (identification.Length == 0)
            {
                throw new ArgumentException(Resources.ImageIdentifierRequired, nameof(identification));
            }

            if (image.Length == 0)
            {
                throw new ArgumentException(Resources.FirmwareImageEmpty, nameof(image));
            }

            options ??= new GXImageUpdateOptions();
            options.Validate();
            // Preflight permissions before initiation or sending any image bytes.
            foreach (int index in new[]
            {
                2,
                3,
                5
            }

            )
            {
                EnsureAccess(target, index, checkAccessRights, 0);
            }

            if (options.Verify || options.Activate || options.Resume)
            {
                EnsureAccess(target, 6, checkAccessRights, 0);
            }

            if (options.Activate || options.Resume)
            {
                EnsureAccess(target, 7, checkAccessRights, 0);
            }

            foreach (int index in new[]
            {
                1,
                2
            }

            )
            {
                EnsureAccess(target, index, checkAccessRights, 2);
            }

            if (options.Verify)
            {
                EnsureAccess(target, 3, checkAccessRights, 2);
            }

            if (options.Activate)
            {
                EnsureAccess(target, 4, checkAccessRights, 2);
            }

            await ReadAsync(target, 5, checkAccessRights).ConfigureAwait(false);
            if (!target.ImageTransferEnabled)
            {
                throw new InvalidOperationException(Resources.ImageTransferDisabled);
            }

            await ReadAsync(target, 2, checkAccessRights).ConfigureAwait(false);
            if (target.ImageBlockSize == 0 || target.ImageBlockSize > int.MaxValue)
            {
                throw new InvalidOperationException(Resources.InvalidImageBlockSize);
            }

            int size = (int)target.ImageBlockSize;
            int blocks = checked((int)(((long)image.Length + size - 1) / size));
            bool resume = false;
            if (options.Resume)
            {
                await ReadAsync(target, 6, checkAccessRights).ConfigureAwait(false);
                await ReadAsync(target, 7, checkAccessRights).ConfigureAwait(false);
                resume = target.ImageTransferStatus == ImageTransferStatus.TransferInitiated && ImageMatches(target, identification, image.Length);
            }

            ReportImageProgress(options, ImageUpdateStage.Initiating, 0, blocks, image.LongLength, size);
            if (!resume)
            {
                await MethodAsync(target, 1, c => target.ImageTransferInitiate(c, identification, image.Length), checkAccessRights).ConfigureAwait(false);
            }

            await ReadAsync(target, 3, checkAccessRights).ConfigureAwait(false);
            for (int pass = 0; ; ++pass)
            {
                int transferred = CountTransferred(target.ImageTransferredBlocksStatus, blocks);
                for (int index = 0; index < blocks; ++index)
                {
                    operationToken.ThrowIfCancellationRequested();
                    string status = target.ImageTransferredBlocksStatus;
                    if (status != null && index < status.Length && status[index] == '1')
                    {
                        continue;
                    }

                    int offset = checked(index * size);
                    int length = Math.Min(size, image.Length - offset);
                    var data = new byte[length];
                    Buffer.BlockCopy(image, offset, data, 0, length);
                    var value = new GXStructure
                    {
                        (uint)index,
                        data
                    };
                    await MethodAsync(target, 2, value, DataType.Structure, checkAccessRights).ConfigureAwait(false);
                    ReportImageProgress(options, ImageUpdateStage.Transferring, ++transferred, blocks, image.LongLength, size);
                }

                await ReadAsync(target, 3, checkAccessRights).ConfigureAwait(false);
                if (CountTransferred(target.ImageTransferredBlocksStatus, blocks) == blocks)
                {
                    break;
                }

                if (pass >= options.MaxBlockTransferRetries)
                {
                    throw new IOException(Resources.ImageBlocksIncomplete);
                }
            }

            if (options.Verify)
            {
                ReportImageProgress(options, ImageUpdateStage.Verifying, blocks, blocks, image.LongLength, size);
                await WithDeadlineAsync(async () =>
                {
                    try
                    {
                        await MethodAsync(target, 3, c => target.ImageVerify(c), checkAccessRights).ConfigureAwait(false);
                    }
                    catch (GXDLMSException error) when (error.ErrorCode == (int)ErrorCode.TemporaryFailure)
                    {
                    }

                    await WaitForImageStatusAsync(target, options.PollInterval, checkAccessRights, ImageTransferStatus.VerificationSuccessful, options.VerificationTimeout, false).ConfigureAwait(false);
                }, options.VerificationTimeout).ConfigureAwait(false);
            }

            if (options.Activate)
            {
                await ReadAsync(target, 6, checkAccessRights).ConfigureAwait(false);
                if (target.ImageTransferStatus != ImageTransferStatus.VerificationSuccessful)
                {
                    throw new InvalidOperationException(Resources.ImageVerificationRequired);
                }

                await ReadAsync(target, 7, checkAccessRights).ConfigureAwait(false);
                if (!ImageMatches(target, identification, image.Length))
                {
                    throw new InvalidOperationException(Resources.VerifiedImageMismatch);
                }

                ReportImageProgress(options, ImageUpdateStage.Activating, blocks, blocks, image.LongLength, size);
                await WithDeadlineAsync(async () =>
                {
                    try
                    {
                        await MethodAsync(target, 4, c => target.ImageActivate(c), checkAccessRights).ConfigureAwait(false);
                    }
                    catch (GXDLMSException error) when (error.ErrorCode == (int)ErrorCode.TemporaryFailure)
                    {
                    }
                    catch (Exception error) when (options.ReconnectAfterActivation && IsFirmwareConnectionFailure(error))
                    {
                        connected = false;
                        try
                        {
                            await Media.CloseAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                    }

                    await WaitForImageStatusAsync(target, options.PollInterval, checkAccessRights, ImageTransferStatus.ActivationSuccessful, options.ActivationTimeout, options.ReconnectAfterActivation).ConfigureAwait(false);
                }, options.ActivationTimeout).ConfigureAwait(false);
            }

            ReportImageProgress(options, ImageUpdateStage.Completed, blocks, blocks, image.LongLength, size);
        }, cancellationToken).ConfigureAwait(false);
        /// <summary>Invokes the optional progress subscribers with the current stage and capped byte progress, isolating each subscriber exception.</summary>
        /// <param name="options">Options containing the optional host progress callback.</param>
        /// <param name="blocks">Total number of image blocks.</param>
        /// <param name="imageLength">Total image size in bytes.</param>
        /// <param name="size">Number of bytes in a full image block.</param>
        /// <param name="stage">Transfer, verification, or activation stage to report.</param>
        /// <param name="transferred">Number of blocks sent or confirmed for the reported stage.</param>
        private static void ReportImageProgress(GXImageUpdateOptions options, ImageUpdateStage stage, int transferred, int blocks, long imageLength, int size)
        {
            var observer = options.OnProgress;
            if (observer != null)
            {
                InvokeObserver(observer, new GXImageUpdateProgress(stage, transferred, blocks, Math.Min(imageLength, (long)transferred * size)));
            }
        }

        /// <summary>Polls the meter until the requested image status is reached, retrying recoverable connection failures when enabled.</summary>
        /// <param name="target">Image-transfer object whose status is polled.</param>
        /// <param name="pollInterval">Delay between status reads and reconnect attempts.</param>
        /// <param name="checkAccessRights">Whether to enforce cached status-read rights, or null to use the reader default.</param>
        /// <param name="expected">Image status that completes polling successfully.</param>
        /// <param name="timeout">Maximum elapsed time allowed for polling and reconnection.</param>
        /// <param name="reconnect">Whether transport failures and a lost association permit reconnection before polling again.</param>
        /// <returns>A task completed when the meter reports the requested status.</returns>
        /// <exception cref="OperationCanceledException">The active operation is canceled.</exception>
        /// <exception cref="TimeoutException">The polling deadline or a communication timeout expires.</exception>
        /// <exception cref="IOException">The association is lost or transport communication fails and reconnection is disabled.</exception>
        /// <exception cref="InvalidOperationException">The meter reports verification or activation failure.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a status read or reconnection request.</exception>
        /// <exception cref="UnauthorizedAccessException">Cached association rights deny reading the image status.</exception>
        private async Task WaitForImageStatusAsync(GXDLMSImageTransfer target, TimeSpan pollInterval, bool? checkAccessRights, ImageTransferStatus expected, TimeSpan timeout, bool reconnect)
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                operationToken.ThrowIfCancellationRequested();
                if (elapsed.Elapsed >= timeout)
                {
                    throw new TimeoutException(string.Format(CultureInfo.CurrentCulture, Resources.FirmwareStatusTimeout, expected));
                }

                try
                {
                    if (!IsConnected)
                    {
                        if (!reconnect)
                        {
                            throw new IOException(Resources.FirmwareVerificationDisconnected);
                        }

                        await ConnectCoreAsync(null, false, operationToken).ConfigureAwait(false);
                    }

                    await ReadAsync(target, 6, checkAccessRights).ConfigureAwait(false);
                    if (target.ImageTransferStatus == expected)
                    {
                        return;
                    }

                    if (target.ImageTransferStatus == ImageTransferStatus.VerificationFailed || target.ImageTransferStatus == ImageTransferStatus.ActivationFailed)
                    {
                        throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Resources.FirmwareStatusFailed, target.ImageTransferStatus));
                    }
                }
                catch (Exception error) when (reconnect && IsFirmwareConnectionFailure(error))
                {
                    connected = false;
                    try
                    {
                        await Media.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                int remaining = (int)Math.Min(int.MaxValue, Math.Max(1, (timeout - elapsed.Elapsed).TotalMilliseconds));
                await WaitAsync(Math.Min(remaining, (int)Math.Ceiling(pollInterval.TotalMilliseconds))).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Counts confirmed blocks within the expected image block range.
        /// </summary>
        /// <param name="status">Meter bitmap where a one marks a received block, or null if unavailable.</param>
        /// <param name="count">Number of image blocks to consider; bitmap entries beyond this range are ignored.</param>
        /// <returns>Number of one bits present within the expected block range.</returns>
        private static int CountTransferred(string status, int count)
        {
            int transferred = 0;
            for (int index = 0; index < count; ++index)
            {
                if (status != null && index < status.Length && status[index] == '1')
                {
                    ++transferred;
                }
            }

            return transferred;
        }

        /// <summary>
        /// Determines whether a firmware error permits a reconnect attempt.
        /// </summary>
        /// <param name="error">Failure encountered while activating or polling the image.</param>
        /// <returns>True for a timeout, transport I/O failure, or lost association; otherwise false.</returns>
        private static bool IsFirmwareConnectionFailure(Exception error) => error is TimeoutException || error is IOException || IsAssociationLost(error);
        /// <summary>
        /// Checks whether the meter lists an activation candidate with the supplied image identifier and size.
        /// </summary>
        /// <param name="target">Image-transfer object containing the activation candidates.</param>
        /// <param name="identification">Identifier bytes to match exactly.</param>
        /// <param name="length">Expected image size in bytes.</param>
        /// <returns>True when at least one candidate has both the expected size and identifier; otherwise false.</returns>
        private static bool ImageMatches(GXDLMSImageTransfer target, byte[] identification, int length) => target.ImageActivateInfo != null && target.ImageActivateInfo.Any(info => info.Size == (uint)length && info.Identification != null && info.Identification.AsSpan().SequenceEqual(identification));

    }
}
