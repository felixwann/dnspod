using System;
using System.Collections.Generic;

namespace dnspod
{
    internal enum CloudProvider { Aliyun, Tencent }

    internal sealed class CloudCredential
    {
        public string Id;
        public string Secret;
        public CloudProvider Provider;
    }

    internal sealed class DnsDomain
    {
        public string Name;
        public string Id;
        public override string ToString() { return Name; }
    }

    internal sealed class DnsRecord
    {
        public string Id;
        public string Type;
        public string Host;
        public string Value;
        public string Line;
        public string CreatedAt;
        public string Ttl;
        public string Priority;
        public string Status;
        public string Weight;
        public string Remark;
        public override string ToString() { return Host + "  " + Type + "  " + Value; }
    }
}
