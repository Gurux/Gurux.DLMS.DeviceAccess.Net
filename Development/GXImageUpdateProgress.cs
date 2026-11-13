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
    /// <summary>Describes progress through the selected phases of one firmware update.</summary>
    public sealed class GXImageUpdateProgress
    {
        /// <summary>Gets the phase currently being reported.</summary>
        public ImageUpdateStage Stage { get; }
        /// <summary>Gets the number of blocks already known or acknowledged as transferred in the current pass.</summary>
        public int TransferredBlocks { get; }
        /// <summary>Gets the number of blocks required by the image size and the meter's block size.</summary>
        public int TotalBlocks { get; }
        /// <summary>Gets the block-count-based byte estimate, capped at the complete image size.</summary>
        /// <remarks>This is a progress estimate, not a transport byte counter; retries do not increase the image size.</remarks>
        public long TransferredBytes { get; }

        /// <summary>Captures one immutable progress report for the firmware-update callback.</summary>
        /// <param name="stage">The phase being reported.</param>
        /// <param name="transferred">The number of known or acknowledged transferred blocks.</param>
        /// <param name="total">The number of blocks in the image.</param>
        /// <param name="bytes">The image-byte progress estimate, excluding protocol overhead.</param>
        internal GXImageUpdateProgress(ImageUpdateStage stage, int transferred, int total, long bytes)
        {
            Stage = stage;
            TransferredBlocks = transferred;
            TotalBlocks = total;
            TransferredBytes = bytes;
        }
    }
}
