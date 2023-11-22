// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET_FRAMEWORK
#pragma warning disable RS0016 // Add public types and members to the declared API

// <auto-generated/>
// Copyright (c) Microsoft Corporation. All rights reserved.
#pragma warning disable 1591

using System.Diagnostics.ContractsLight;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Wrapper around the CAPI implementation of the SHA-256 hashing algorithm
    /// </summary>
    [System.Security.Permissions.HostProtection(MayLeakOnAbort = true)]
    public sealed class SHA256CryptoServiceProvider : SHA256
    {
        private readonly CapiHashAlgorithm m_hashAlgorithm;

        public SHA256CryptoServiceProvider()
        {
            m_hashAlgorithm = new CapiHashAlgorithm(CapiNative.ProviderNames.MicrosoftEnhancedRsaAes,
                                                    CapiNative.ProviderType.RsaAes,
                                                    CapiNative.AlgorithmId.Sha256);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    m_hashAlgorithm.Dispose();
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <summary>
        ///     Reset the hash algorithm to begin hashing a new set of data
        /// </summary>
        public override void Initialize()
        {
            Contract.Assert(m_hashAlgorithm != null);
            m_hashAlgorithm.Initialize();
        }

        /// <summary>
        ///     Hash a block of data
        /// </summary>
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            Contract.Assert(m_hashAlgorithm != null);
            m_hashAlgorithm.HashCore(array, ibStart, cbSize);
        }

        /// <summary>
        ///     Complete the hash, returning its value
        /// </summary>
        protected override byte[] HashFinal()
        {
            Contract.Assert(m_hashAlgorithm != null);
            return m_hashAlgorithm.HashFinal();
        }
    }
}

#pragma warning restore RS0016 // Add public types and members to the declared API
#endif
