using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace dnspod
{
    internal static class DnsProviders
    {
        public static IList<DnsDomain> Domains(CloudCredential c) { return c.Provider == CloudProvider.Aliyun ? AliDomains(c) : TencentDomains(c); }
        public static IList<DnsRecord> Records(CloudCredential c, DnsDomain d) { return c.Provider == CloudProvider.Aliyun ? AliRecords(c, d) : TencentRecords(c, d); }
        public static void Add(CloudCredential c, DnsDomain d, string host, string type, string value) { if (c.Provider == CloudProvider.Aliyun) AliAdd(c, d, host, type, value); else TencentAdd(c, d, host, type, value); }
        public static void Delete(CloudCredential c, DnsDomain d, DnsRecord r) { CheckRecord(c, d, r); if (c.Provider == CloudProvider.Aliyun) Json(AliCall(c, "DeleteDomainRecord", new Dictionary<string, string> { { "RecordId", r.Id } })); else TencentCall(c, "DeleteRecord", new Dictionary<string, string> { { "Domain", d.Name }, { "RecordId", r.Id } }); }

        private static void CheckRecord(CloudCredential c, DnsDomain d, DnsRecord original)
        {
            foreach (DnsRecord record in Records(c, d))
                if (record.Id == original.Id && record.Host == original.Host && record.Type == original.Type && record.Value == original.Value && record.Line == original.Line && record.Ttl == original.Ttl && record.Priority == original.Priority && record.Status == original.Status && record.Weight == original.Weight && record.Remark == original.Remark) return;
            throw new InvalidOperationException("记录已变化或不存在，请刷新列表后重新操作。");
        }
        public static void Update(CloudCredential c, DnsDomain d, DnsRecord r, string host, string type, string value, string remark)
        {
            if (String.IsNullOrEmpty(host) || String.IsNullOrEmpty(type) || String.IsNullOrEmpty(value)) throw new InvalidOperationException("主机记录、类型和记录值不能为空。");
            bool recordChanged = r.Host != host || r.Type != type || r.Value != value;
            bool remarkChanged = (r.Remark ?? "") != (remark ?? "");
            if (!recordChanged && !remarkChanged) return;
            CheckRecord(c, d, r);
            if (recordChanged)
            {
                Dictionary<string, string> args = new Dictionary<string, string> { { "RecordId", r.Id }, { "Value", value } };
                if (!String.IsNullOrEmpty(r.Ttl)) args["TTL"] = r.Ttl;
                if (c.Provider == CloudProvider.Aliyun)
                {
                    args["RR"] = host; args["Type"] = type; args["Line"] = r.Line;
                    if (type == "MX") args["Priority"] = String.IsNullOrEmpty(r.Priority) ? "10" : r.Priority;
                    Json(AliCall(c, "UpdateDomainRecord", args));
                }
                else
                {
                    args["Domain"] = d.Name; args["SubDomain"] = host; args["RecordType"] = type; args["RecordLine"] = r.Line;
                    if (type == "MX" || type == "HTTPS" || type == "SVCB") args["MX"] = String.IsNullOrEmpty(r.Priority) ? "10" : r.Priority;
                    if (!String.IsNullOrEmpty(r.Status)) args["Status"] = r.Status;
                    if (!String.IsNullOrEmpty(r.Weight)) args["Weight"] = r.Weight;
                    TencentCall(c, "ModifyRecord", args);
                }
            }
            if (remarkChanged)
            {
                try
                {
                    Dictionary<string, string> args = new Dictionary<string, string> { { "RecordId", r.Id }, { "Remark", remark ?? "" } };
                    if (c.Provider == CloudProvider.Aliyun) Json(AliCall(c, "UpdateDomainRecordRemark", args));
                    else { args["Domain"] = d.Name; TencentCall(c, "ModifyRecordRemark", args); }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException((recordChanged ? "解析内容已更新，但备注未确认保存。" : "备注未确认保存。") + "请刷新核实后再操作。\n\n" + ex.Message, ex);
                }
            }
        }
        private static IList<DnsDomain> AliDomains(CloudCredential c)
        {
            IDictionary root = Json(AliCall(c, "DescribeDomains", new Dictionary<string, string>())); IList<DnsDomain> list = new List<DnsDomain>();
            foreach (IDictionary x in Items(root, "Domains", "Domain")) list.Add(new DnsDomain { Name = S(x, "DomainName"), Id = S(x, "DomainId") }); return list;
        }
        private static IList<DnsRecord> AliRecords(CloudCredential c, DnsDomain d)
        {
            IList<DnsRecord> list = new List<DnsRecord>();
            for (int page = 1; ; page++)
            {
                IDictionary root = Json(AliCall(c, "DescribeDomainRecords", new Dictionary<string, string> { { "DomainName", d.Name }, { "PageNumber", page.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "PageSize", "100" } }));
                IList batch = Items(root, "DomainRecords", "Record");
                foreach (IDictionary x in batch) list.Add(new DnsRecord { Id = S(x, "RecordId"), Type = S(x, "Type"), Host = S(x, "RR"), Value = S(x, "Value"), Line = S(x, "Line"), CreatedAt = S(x, "Status"), Ttl = S(x, "TTL"), Priority = S(x, "Priority"), Status = S(x, "Status"), Remark = S(x, "Remark") });
                if (batch.Count < 100) return list;
            }
        }
        private static void AliAdd(CloudCredential c, DnsDomain d, string host, string type, string value) { AliCall(c, "AddDomainRecord", new Dictionary<string, string> { { "DomainName", d.Name }, { "RR", host }, { "Type", type }, { "Value", value }, { "Line", "默认" } }); }
        private static string AliCall(CloudCredential c, string action, IDictionary<string, string> args)
        {
            string date = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"); args["AccessKeyId"] = c.Id; args["Format"] = "JSON"; args["SignatureMethod"] = "HMAC-SHA1"; args["SignatureNonce"] = Guid.NewGuid().ToString(); args["SignatureVersion"] = "1.0"; args["SignatureType"] = "HMAC-SHA1"; args["Timestamp"] = date; args["Version"] = "2015-01-09"; args["Action"] = action;
            SortedDictionary<string, string> sorted = new SortedDictionary<string, string>(args); StringBuilder q = new StringBuilder(); foreach (KeyValuePair<string, string> p in sorted) q.Append('&').Append(Enc(p.Key)).Append('=').Append(Enc(p.Value));
            string src = "GET&%2F&" + Enc(q.ToString().Substring(1)); string sign = HmacSha1(c.Secret + "&", src); q.Append("&Signature=").Append(Enc(sign)); return Request("https://alidns.aliyuncs.com/?" + q.ToString().Substring(1));
        }
        private static IList<DnsDomain> TencentDomains(CloudCredential c)
        {
            IList<DnsDomain> list = new List<DnsDomain>();
            for (int offset = 0; ; offset += 100)
            {
                IDictionary r = Json(TencentCall(c, "DescribeDomainList", new Dictionary<string, string> { { "Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "Limit", "100" } }));
                IList batch = Items(r, "DomainList");
                foreach (IDictionary x in batch) list.Add(new DnsDomain { Name = S(x, "Name"), Id = S(x, "DomainId") });
                if (batch.Count < 100) return list;
            }
        }
        private static IList<DnsRecord> TencentRecords(CloudCredential c, DnsDomain d)
        {
            IList<DnsRecord> list = new List<DnsRecord>();
            for (int offset = 0; ; offset += 100)
            {
                IDictionary r = Json(TencentCall(c, "DescribeRecordList", new Dictionary<string, string> { { "Domain", d.Name }, { "Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "Limit", "100" }, { "ErrorOnEmpty", "no" } }));
                IList batch = Items(r, "RecordList");
                foreach (IDictionary x in batch) list.Add(new DnsRecord { Id = S(x, "RecordId"), Type = S(x, "Type"), Host = S(x, "Name"), Value = S(x, "Value"), Line = S(x, "Line"), CreatedAt = S(x, "UpdatedOn"), Ttl = S(x, "TTL"), Priority = S(x, "MX"), Status = S(x, "Status"), Weight = S(x, "Weight"), Remark = S(x, "Remark") });
                if (batch.Count < 100) return list;
            }
        }
        private static void TencentAdd(CloudCredential c, DnsDomain d, string host, string type, string value) { Dictionary<string, string> args = new Dictionary<string, string> { { "Domain", d.Name }, { "RecordType", type }, { "RecordLine", "默认" }, { "Value", value }, { "SubDomain", host } }; if (type == "MX") args["MX"] = "10"; TencentCall(c, "CreateRecord", args); }
        private static string TencentCall(CloudCredential c, string action, IDictionary<string, string> args)
        {
            string service = "dnspod", host = "dnspod.tencentcloudapi.com", payload = JsonEncode(args);
            DateTime now = DateTime.UtcNow;
            string timestamp = ((now - new DateTime(1970, 1, 1)).Ticks / TimeSpan.TicksPerSecond).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string date = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            string canonical = "POST\n/\n\ncontent-type:application/json\nhost:" + host + "\n\ncontent-type;host\n" + Sha256(payload);
            string credential = date + "/" + service + "/tc3_request";
            string message = "TC3-HMAC-SHA256\n" + timestamp + "\n" + credential + "\n" + Sha256(canonical);
            byte[] key = Encoding.UTF8.GetBytes("TC3" + c.Secret);
            foreach (string part in new string[] { date, service, "tc3_request" })
                using (HMACSHA256 h = new HMACSHA256(key)) key = h.ComputeHash(Encoding.UTF8.GetBytes(part));
            string sign;
            using (HMACSHA256 h = new HMACSHA256(key)) sign = Hex(h.ComputeHash(Encoding.UTF8.GetBytes(message)));
            string auth = "TC3-HMAC-SHA256 Credential=" + c.Id + "/" + credential + ", SignedHeaders=content-type;host, Signature=" + sign;
            string response = Request("https://" + host, "POST", payload, new Dictionary<string, string> { { "Authorization", auth }, { "X-TC-Action", action }, { "X-TC-Timestamp", timestamp }, { "X-TC-Version", "2021-03-23" }, { "Content-Type", "application/json" } });
            Json(response);
            return response;
        }
        private static readonly object networkLogLock = new object();
        [System.Diagnostics.Conditional("DEBUG")]
        private static void NetworkLog(string requestId, string content)
        {
            try
            {
                content = System.Text.RegularExpressions.Regex.Replace(content, @"(?im)^(Authorization|Proxy-Authorization|Cookie|Set-Cookie):[^\r\n]*", "$1: [REDACTED]");
                content = System.Text.RegularExpressions.Regex.Replace(content, @"(?i)([?&](?:AccessKeyId|Signature|SecretId|SecretKey|SecurityToken)=)[^&\s]*", "$1[REDACTED]");
                lock (networkLogLock)
                {
                    string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, "network-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + " [" + requestId + "] " + content + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Network log write failed: " + ex.Message); }
        }
        private static string Request(string url, string method = "GET", string body = null, IDictionary<string, string> headers = null)
        {
            string requestId = Guid.NewGuid().ToString("N");
            StringBuilder requestLog = new StringBuilder("REQUEST ").Append(method).Append(' ').Append(url).AppendLine();
            if (headers != null) foreach (KeyValuePair<string, string> h in headers) requestLog.Append(h.Key).Append(": ").AppendLine(h.Value);
            requestLog.AppendLine().Append(body);
            NetworkLog(requestId, requestLog.ToString());
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url); r.Method = method; r.Timeout = 15000; r.ReadWriteTimeout = 15000;
                if (headers != null) foreach (KeyValuePair<string, string> h in headers)
                {
                    if (String.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) r.ContentType = h.Value;
                    else r.Headers[h.Key] = h.Value;
                }
                if (body != null) { byte[] b = Encoding.UTF8.GetBytes(body); r.ContentLength = b.Length; using (Stream s = r.GetRequestStream()) s.Write(b, 0, b.Length); }
                using (HttpWebResponse x = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(x.GetResponseStream(), Encoding.UTF8))
                {
                    string responseBody = sr.ReadToEnd();
                    NetworkLog(requestId, "RESPONSE " + (int)x.StatusCode + " " + x.StatusDescription + Environment.NewLine + x.Headers + Environment.NewLine + responseBody);
                    return responseBody;
                }
            }
            catch (WebException ex)
            {
                NetworkLog(requestId, "ERROR " + ex);
                string message = ex.Message; HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null) using (response) using (StreamReader sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string bodyText = sr.ReadToEnd();
                    NetworkLog(requestId, "RESPONSE " + (int)response.StatusCode + " " + response.StatusDescription + Environment.NewLine + response.Headers + Environment.NewLine + bodyText);
                    try { IDictionary error = Json(bodyText); message = S(error, "Message"); if (String.IsNullOrEmpty(message) && error.Contains("Response")) message = S((IDictionary)error["Response"], "Message"); }
                    catch { if (!String.IsNullOrEmpty(bodyText)) message = bodyText; }
                }
                throw new InvalidOperationException("云 API 请求失败：" + message, ex);
            }
            catch (Exception ex) { NetworkLog(requestId, "ERROR " + ex); throw; }
        }
        private static IDictionary Json(string s) { if (String.IsNullOrEmpty(s)) throw new InvalidOperationException("云 API 返回为空。"); object o = new JavaScriptSerializer().DeserializeObject(s); IDictionary r = o as IDictionary; if (r == null) throw new InvalidOperationException("云 API 返回格式无效。"); if (r.Contains("Response")) { IDictionary response = r["Response"] as IDictionary; if (response == null) throw new InvalidOperationException("腾讯云 API 返回的 Response 无效。"); if (response.Contains("Error")) { IDictionary error = response["Error"] as IDictionary; string message = S(error, "Message"); throw new InvalidOperationException(String.IsNullOrEmpty(message) ? "腾讯云 API 返回错误。" : message); } return response; } if (r.Contains("Code") && S(r, "Code") != "OK" && r.Contains("Message")) throw new InvalidOperationException(String.IsNullOrEmpty(S(r, "Message")) ? "阿里云 API 返回错误。" : S(r, "Message")); return r; }
        private static string JsonEncode(IDictionary<string, string> v) { Dictionary<string, object> values = new Dictionary<string, object>(); foreach (KeyValuePair<string, string> p in v) { if (p.Key == "RecordId" || p.Key == "Offset" || p.Key == "Limit" || p.Key == "MX" || p.Key == "TTL" || p.Key == "Weight") values[p.Key] = Int64.Parse(p.Value, System.Globalization.CultureInfo.InvariantCulture); else values[p.Key] = p.Value; } return new JavaScriptSerializer().Serialize(values); }
        private static IList Items(IDictionary x, params string[] path) { object o = x; foreach (string p in path) { IDictionary d = o as IDictionary; if (d == null || !d.Contains(p)) return new ArrayList(); o = d[p]; } return o as IList ?? new ArrayList(); }
        private static string S(IDictionary x, string key) { return x != null && x.Contains(key) && x[key] != null ? Convert.ToString(x[key]) : ""; }
        private static string Enc(string s) { return Uri.EscapeDataString(s).Replace("*", "%2A").Replace("%7E", "~"); }
        private static string HmacSha1(string key, string value) { using (HMACSHA1 h = new HMACSHA1(Encoding.UTF8.GetBytes(key))) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        private static string Sha256(string value) { using (SHA256 h = SHA256.Create()) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        private static string Hex(byte[] b) { StringBuilder x = new StringBuilder(); foreach (byte n in b) x.Append(n.ToString("x2")); return x.ToString(); }
    }
}
