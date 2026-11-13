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

using System;

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>Controls firmware transfer, verification and activation. All waits can be canceled.</summary>
    public sealed class GXImageUpdateOptions
    {
        /// <summary>Gets or sets whether the image is verified after all blocks are confirmed; defaults to true.</summary>
        public bool Verify { get; set; } = true;
        /// <summary>Gets or sets whether to activate the image and await confirmed activation; defaults to true.</summary>
        /// <remarks>Activation requires a matching image identifier and size and a successful verification status.</remarks>
        public bool Activate { get; set; } = true;
        /// <summary>Resume only when the meter advertises the same image identifier and size.</summary>
        public bool Resume { get; set; }
        /// <summary>Gets or sets whether a connection lost during activation is reopened to read the final status.</summary>
        /// <remarks>The activation action itself is not repeated.</remarks>
        public bool ReconnectAfterActivation { get; set; } = true;
        /// <summary>Gets or sets the positive interval between status polls; defaults to one second.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>Gets or sets the positive deadline for the verification action and status exchanges; defaults to 30 minutes.</summary>
        public TimeSpan VerificationTimeout { get; set; } = TimeSpan.FromMinutes(30);
        /// <summary>Gets or sets the positive deadline for activation and its confirmation exchanges; defaults to 30 minutes.</summary>
        /// <remarks>Opening a custom synchronous transport remains subject to that transport's cancellation and timeout support.</remarks>
        public TimeSpan ActivationTimeout { get; set; } = TimeSpan.FromMinutes(30);
        /// <summary>Gets or sets the nonnegative number of additional passes for retransmitting missing blocks; defaults to three.</summary>
        public int MaxBlockTransferRetries { get; set; } = 3;
        /// <summary>Gets or sets the synchronous callback receiving transfer and phase progress.</summary>
        /// <remarks>Marshal UI updates when necessary; do not start another meter operation from this callback. Each subscriber exception is ignored so reporting cannot interrupt the update or other subscribers. Use the update cancellation token to cancel.</remarks>
        public Action<GXImageUpdateProgress>? OnProgress { get; set; }

        /// <summary>Rejects polling intervals, deadlines or retry counts that cannot be used by the update loop.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The poll interval is nonpositive or exceeds the supported millisecond range,
        /// a phase timeout is nonpositive, or the retry count is negative.
        /// </exception>
        internal void Validate()
        {
            if (PollInterval <= TimeSpan.Zero || PollInterval.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(PollInterval));
            }

            if (VerificationTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(VerificationTimeout));
            }

            if (ActivationTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(ActivationTimeout));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(MaxBlockTransferRetries);
        }
    }
}
