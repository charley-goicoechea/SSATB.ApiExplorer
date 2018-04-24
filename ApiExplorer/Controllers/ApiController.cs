using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSATB.ApiExplorer.Models;
using SSATB.Configuration;
using System.Threading.Tasks;

namespace ApiExplorer.Controllers
{
    public class ApiController : Controller
    {
        string AccessToken
        {
            get { return Session["access_token"] as string; }
            set { Session["access_token"] = value; }
        }

        string RefreshToken
        {
            get { return Session["refresh_token"] as string; }
            set { Session["refresh_token"] = value; }
        }

        string AuthorizationServerEndpoint
        {
            get { return Config.GetValue("AuthorizationServerUrl"); }
        }

        string ClientId
        {
            get { return Config.GetValue("ClientId"); }
        }
        
        string ClientSecret
        {
            get { return Config.GetValue("ClientSecret"); }
        }

        string RedirectUrl
        {
            get { return VirtualPathUtility.AppendTrailingSlash(Config.GetValue("ServiceUrl")) + "authorizeresponse"; }
        }

        public static IEnumerable<ApiService> Services
        {
            get
            {
                var json = Config.GetValue("ApiList");
                var list = JsonConvert.DeserializeObject<IEnumerable<ApiService>>(json);
                return list;
            }
        }

        [Route("")]
        public ActionResult DevResources()
        {
            ViewBag.AuthorizationServerEndpoint = this.AuthorizationServerEndpoint;
            ViewBag.Title = "Developer Resources";
            return View();
        }

        [Route("{service}")]
        public ActionResult Index(string service)
        {
            if (string.IsNullOrEmpty(service))
                return Redirect("~/" + Services.First().Name);

            var api = Services.FirstOrDefault(s => s.Name.ToLower() == service.ToLower());
            if (api == null)
                return HttpNotFound();

            if (string.IsNullOrEmpty(this.AccessToken))
            {
                string redirect = Url.Action("Login", new { referrer = Request.Url.ToString() });
                return Redirect(redirect);
            }

            ViewBag.AccessToken = this.AccessToken;
            ViewBag.Title = service;
            ViewBag.ApiDocsUrl = string.Format("/{0}/docs", service);
            return View();
        }

        [Route("login")]
        public ActionResult Login(string referrer)
        {
            if (string.IsNullOrEmpty(referrer))
                referrer = "/";

            //string.Format("{0}/authorize?response_type=code&client_id={1}&redirect_uri={2}&scope=openid%20profile%20offline_access&state={3}",
            ViewBag.AuthorizeUrl = string.Format("{0}/authorize?response_type=code&client_id={1}&redirect_uri={2}&scope=openid%20profile%20offline_access%20charley_scope&state={3}",
                    AuthorizationServerEndpoint,
                    ClientId,
                    RedirectUrl,
                    HttpUtility.UrlEncode(referrer));

            ViewBag.Referrer = referrer;

            return View(new ApiCredentials());
        }

        [Route("login"), HttpPost]
        public async Task<ActionResult> Login(ApiCredentials credentials, string referrer)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    using (var client = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }))
                    {
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
                        var content = string.Format("client_id={0}&client_secret={1}&grant_type=client_credentials&scope={2}",
                            HttpUtility.UrlEncode(credentials.ClientId),
                            HttpUtility.UrlEncode(credentials.ClientSecret),
                            HttpUtility.UrlEncode(credentials.OrgId));
                        HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/x-www-form-urlencoded");
                        HttpResponseMessage response = await client.PostAsync(AuthorizationServerEndpoint + "/token", httpContent);
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorMessage = response.StatusCode == HttpStatusCode.InternalServerError ?
                                "Invalid credentials" :
                                await response.Content.ReadAsStringAsync();
                            throw new ApplicationException(errorMessage);
                        }

                        var tokenjson = await response.Content.ReadAsStringAsync();
                        var jobj = JObject.Parse(tokenjson);

                        AccessToken = jobj["access_token"].ToString();

                        if (string.IsNullOrEmpty(referrer))
                            referrer = "/";

                        return Redirect(referrer); // state parameter contains the original visited page that triggered the OAuth handshake
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
            }
            ViewBag.Referrer = referrer;
            return View(credentials);
        }

        [Route("authorizeresponse")]
        public async Task<ActionResult> AuthorizeResponse(string code, string state)
        {
            using (var client = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }))
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
                var content = string.Format("client_id={0}&client_secret={1}&grant_type=authorization_code&code={2}&redirect_uri={3}",
                    HttpUtility.UrlEncode(ClientId),
                    HttpUtility.UrlEncode(ClientSecret),
                    HttpUtility.UrlEncode(code),
                    HttpUtility.UrlEncode(RedirectUrl));
                HttpContent httpContent = new StringContent(content, Encoding.UTF8, "application/x-www-form-urlencoded");
                HttpResponseMessage response = await client.PostAsync(AuthorizationServerEndpoint + "/token", httpContent);
                response.EnsureSuccessStatusCode();

                var tokenjson = await response.Content.ReadAsStringAsync();
                var jobj = JObject.Parse(tokenjson);

                AccessToken = jobj["access_token"].ToString();
                RefreshToken = jobj["refresh_token"].ToString();

                return Redirect(state); // state parameter contains the original visited page that triggered the OAuth handshake
            }
        }

        [Route("logout")]
        public ActionResult Logout()
        {
            var url = string.Format("{0}/endsession?access_token={1}&post_logout_redirect_uri={2}",
                AuthorizationServerEndpoint,
                HttpUtility.UrlEncode(this.AccessToken),
                HttpUtility.UrlEncode(Request.Url.GetLeftPart(UriPartial.Authority)));

            Session.Clear();
            return Redirect(url);
        }

        [Route("{service}/docs")]
        public async Task<ActionResult> GetApiDocs(string service)
        {
            var api = Services.FirstOrDefault(s => s.Name.ToLower() == service.ToLower());
            if (api == null)
                return HttpNotFound();

            using (var client = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }))
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
                HttpResponseMessage response = await client.GetAsync(getDocsUrl(api.Url));
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();

                // the swagger api Docs is returned directly from the micro-service -- we need to fix up the paths to get it to work correctly
                var jobj = JObject.Parse(json);

                //rewrite the host and scheme to match the Api proxy endpoint that it is being routed through
                var uri = new System.Uri(api.Url);
                jobj["host"] = uri.Host;
                jobj["schemes"] = new JArray(uri.Scheme);

                // rewrite the paths to prepend the servicename
                var newpaths = new JObject();
                var paths = jobj["paths"] as JObject;
                foreach (KeyValuePair<string, JToken> prop in paths)
                    newpaths.Add(new JProperty(uri.AbsolutePath + prop.Key, prop.Value));
                jobj["paths"] = newpaths;

                // filter out private parameters
                findAndPerform(newpaths, "parameters", j =>
                {
                    var arr = j as JArray;
                    if (arr == null) return;
                    foreach (var parameter in arr.ToList())
                    {
                        var p = parameter.SelectToken("private");
                        if (p != null && p.Value<bool>() == true)
                            parameter.Remove();
                    }
                });

                return Content(jobj.ToString(), "application/json");
            }
        }

        void findAndPerform(JToken o, string name, Action<JToken> action)
        {
            if (o is JObject)
            {
                foreach (KeyValuePair<string, JToken> prop in (o as JObject))
                {
                    if (prop.Key == name)
                        action(prop.Value);
                    else
                        findAndPerform(prop.Value, name, action);
                }
            }
            else if (o is JArray)
            {
                foreach (var j in (o as JArray))
                {
                    findAndPerform(j, name, action);
                }
            }
        }

        string getDocsUrl(string apiUrl)
        {
            return VirtualPathUtility.AppendTrailingSlash(apiUrl) + "swagger/docs/v1";
        }
    }
}