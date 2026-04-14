using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aerochat.Enums
{
    public enum AerochatLoginStatus
    {
        Success,
        UnknownFailure,
        Unauthorized,
        ServerError,
        /// <summary>Connect did not complete within the client timeout.</summary>
        ConnectionTimeout,
        /// <summary>Failure while negotiating TLS / a secure channel (not generic network errors).</summary>
        TlsHandshakeFailure,
        BadRequest,
    }
}
