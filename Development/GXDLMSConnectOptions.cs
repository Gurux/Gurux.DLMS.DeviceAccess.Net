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

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>Configures connection establishment and optional association-view discovery.</summary>
    /// <remarks>The reader copies handshake settings when connection setup executes and reuses them for internal reconnects. Omitting options preserves the last handshake settings.</remarks>
    public sealed class GXDLMSConnectOptions
    {
        /// <summary>Gets or sets the positive transport-open and packet timeout during connection establishment in milliseconds; defaults to 5000.</summary>
        public int WaitTime { get; set; } = 5000;

        /// <summary>Gets or sets the positive maximum number of attempts for a rejected connection packet; defaults to three.</summary>
        /// <remarks>Response timeouts are not retried because the meter might already have processed the request.</remarks>
        public int RetryCount { get; set; } = 3;

        /// <summary>Gets or sets the nonnegative readiness delay before each HDLC SNRM request in milliseconds; defaults to 20. Zero disables the delay.</summary>
        /// <remarks>Allows serial optical heads and meters to become ready after opening or disconnecting a public invocation-counter association. Interfaces that do not send SNRM are unaffected.</remarks>
        public int HdlcConnectionDelay { get; set; } = 20;

        /// <summary>Gets or sets the nonnegative delay before sending the optical identification request in milliseconds; defaults to 1000. Zero disables the delay.</summary>
        public int OpticalHeadInitialDelay { get; set; } = 1000;

        /// <summary>Gets or sets the nonnegative delay after sending the mode E acknowledgement and before receiving its optional echo in milliseconds; defaults to 200. Zero disables the delay.</summary>
        public int OpticalHeadSwitchDelay { get; set; } = 200;

        /// <summary>Gets or sets the nonnegative delay after switching to the negotiated serial settings in milliseconds; defaults to 800. Zero disables the delay.</summary>
        public int OpticalHeadReadyDelay { get; set; } = 800;

        /// <summary>Gets or sets the positive receive timeout for the optional mode E acknowledgement echo in milliseconds; defaults to 2000.</summary>
        public int OpticalHeadEchoWaitTime { get; set; } = 2000;

        /// <summary>Gets or sets the positive receive timeout for draining an invalid optical identification reply in milliseconds; defaults to 100.</summary>
        public int OpticalHeadInvalidResponseWaitTime { get; set; } = 100;
        /// <summary>Gets or sets the logical name of the public invocation-counter attribute read before a secured connection.</summary>
        /// <remarks>A null or empty value disables invocation-counter discovery.</remarks>
        public string? InvocationCounter { get; set; }

        /// <summary>Gets or sets whether SNRM negotiation is skipped for a pre-established association; defaults to false.</summary>
        public bool IgnoreSNRMWithPreEstablished { get; set; }

        /// <summary>Gets or sets whether to load a missing association view after connecting; defaults to true.</summary>
        public bool ReadAssociationView { get; set; } = true;
    }
}
