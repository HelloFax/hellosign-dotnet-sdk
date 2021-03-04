using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Linq;
using RestSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HelloSign
{
    internal class Tools
    {
        /// <summary>
        /// UTC DateTime instance for the Unix time epoch (1970-1-1).
        /// 
        /// Note: If target changes to .NET Framework 4.6 or higher, replace me with:
        /// https://msdn.microsoft.com/en-us/library/system.datetimeoffset.fromunixtimeseconds.aspx
        /// </summary>
        public static DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Returns a UTC DateTime instance from a Unix timestamp (in seconds).
        /// 
        /// Note: If target changes to .NET Framework 4.6 or higher, replace me with:
        /// https://msdn.microsoft.com/en-us/library/system.datetimeoffset.fromunixtimeseconds.aspx
        /// </summary>
        /// <param name="timestamp">A Unix timestamp in seconds.</param>
        public static DateTime UnixTimeToDateTime(int timestamp)
        {
            return Epoch.AddSeconds(timestamp);
        }
    }

    /// <summary>
    /// Wrapper for interacting with the HelloSign API.
    /// </summary>
    public class Client
    {
        /// <summary>
        /// Specifies different HelloSign environments that can be reached.
        /// </summary>
        public enum Environment {
            Prod,
            QA,
            Staging,
            Dev
        }

        private string apiKey;
        private RestClient client;
        private RestSharp.Deserializers.JsonDeserializer deserializer;
        private const string defaultHost = "api.hellosign.com";
        public List<Warning> Warnings { get; private set; }
        public string Version { get; private set; }

        /// <summary>
        /// Additional request parameters you wish to inject into your API calls.
        /// 
        /// Recommended only for advanced users who need to make requests not otherwise
        /// made possible by this library.
        /// 
        /// These will affect all subsequent API calls you make using this client
        /// instance, so remember to clear between requests as needed.
        /// </summary>
        public Dictionary<string, string> AdditionalParameters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Default constructor with no authentication.
        ///
        /// Limited to unauthenticated calls only unless <see cref="UseApiKeyAuthentication"/>
        /// or <see cref="UseOAuth2Authentication"/> is subsequently called.
        /// </summary>
        public Client()
        {
            // Determine product version
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            Version = fvi.ProductVersion;

            // Initialize stuff
            client = new RestClient();
            client.UserAgent = "hellosign-dotnet-sdk/" + Version;
            deserializer = new RestSharp.Deserializers.JsonDeserializer();
            Warnings = new List<Warning>();
            SetApiHost(defaultHost);
        }

        /// <summary>
        /// Constructor initialized with API key authentication.
        /// </summary>
        /// <param name="apiKey">Your HelloSign account API key.</param>
        public Client(string apiKey) : this()
        {
            this.UseApiKeyAuthentication(apiKey);
        }

        /// <summary>
        /// Use an API Key to authenticate future requests.
        /// See: https://app.hellosign.com/api/documentation#Authentication
        /// </summary>
        /// <param name="apiKey">Your HelloSign account API key.</param>
        public void UseApiKeyAuthentication(string apiKey)
        {
            this.apiKey = apiKey;
            client.Authenticator = new RestSharp.Authenticators.HttpBasicAuthenticator(apiKey, "");
        }

        /// <summary>
        /// Use an OAuth 2.0 Access Token to authenticate future requests.
        /// See: https://app.hellosign.com/api/oauthWalkthrough
        /// </summary>
        /// <see href="https://app.hellosign.com/api/oauthWalkthrough"></see>
        /// <param name="accessToken"></param>
        public void UseOAuth2Authentication(string accessToken)
        {
            client.Authenticator = new RestSharp.Authenticators.OAuth2AuthorizationRequestHeaderAuthenticator(accessToken, "Bearer");
        }

        private void HandleErrors(IRestResponse response)
        {
            // If there was an exception getting the response
            if (response.ErrorException != null)
            {
                const string message = "Error retrieving response.  Check inner details for more info.";
                throw new ApplicationException(message, response.ErrorException);
            }

            // Check for errors/warnings from HelloSign
            if (response.ContentType == "application/json")
            {
                // Check for an error
                deserializer.RootElement = "error";
                var error = deserializer.Deserialize<Error>(response);
                if (error.ErrorName != null)
                {
                    switch (error.ErrorName)
                    {
                        case "bad_request":
                            throw new BadRequestException(error.ErrorMsg, error.ErrorName);
                        case "unauthorized":
                            throw new UnauthorizedException(error.ErrorMsg, error.ErrorName);
                        case "payment_required":
                            throw new PaymentRequiredException(error.ErrorMsg, error.ErrorName);
                        case "forbidden":
                            throw new ForbiddenException(error.ErrorMsg, error.ErrorName);
                        case "not_found":
                            throw new NotFoundException(error.ErrorMsg, error.ErrorName);
                        case "conflict":
                            throw new ConflictException(error.ErrorMsg, error.ErrorName);
                        case "team_invite_failed":
                            throw new ForbiddenException(error.ErrorMsg, error.ErrorName);
                        case "invalid_recipient":
                            throw new BadRequestException(error.ErrorMsg, error.ErrorName);
                        case "signature_request_cancel_failed":
                            throw new BadRequestException(error.ErrorMsg, error.ErrorName);
                        case "maintenance":
                            throw new ServiceUnavailableException(error.ErrorMsg, error.ErrorName);
                        case "deleted":
                            throw new GoneException(error.ErrorMsg, error.ErrorName);
                        case "unknown":
                            throw new UnknownException(error.ErrorMsg, error.ErrorName);
                        case "method_not_supported":
                            throw new MethodNotAllowedException(error.ErrorMsg, error.ErrorName);
                        case "signature_request_invalid":
                            throw new ErrorException(error.ErrorMsg, error.ErrorName);
                        case "template_error":
                            throw new ErrorException(error.ErrorMsg, error.ErrorName);
                        case "invalid_reminder":
                            throw new BadRequestException(error.ErrorMsg, error.ErrorName);
                        case "exceeded_rate":
                            throw new ExceededRateLimitException(error.ErrorMsg, error.ErrorName);
                        default:
                            throw new ErrorException(error.ErrorMsg, error.ErrorName);
                    }
                }

                // Look for warnings
                deserializer.RootElement = "warnings";
                var warnings = deserializer.Deserialize<List<Warning>>(response);
                if (warnings[0].WarningName != null)
                {
                    Warnings.AddRange(warnings);
                }
            }

            // Throw exceptions appropriate for the HTTP status code
            switch (response.StatusCode)
            {
                case HttpStatusCode.BadRequest:
                    throw new BadRequestException();
                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedException();
                case HttpStatusCode.PaymentRequired:
                    throw new PaymentRequiredException();
                case HttpStatusCode.Forbidden:
                    throw new ForbiddenException();
                case HttpStatusCode.NotFound:
                    throw new NotFoundException();
                case HttpStatusCode.MethodNotAllowed:
                    throw new MethodNotAllowedException();
                case HttpStatusCode.Conflict:
                    throw new ConflictException();
                case HttpStatusCode.Gone:
                    throw new GoneException();
                case HttpStatusCode.InternalServerError:
                    throw new UnknownException();
            }

            // Throw an exception for any non-2xx status code we didn't cover above
            var statusCode = (int)response.StatusCode;
            if ((statusCode < 200) || (statusCode >= 300))
            {
                throw new ApplicationException("Received status " + statusCode + " from server. Full response:\n" + response.Content);
            }
        }

        private void InjectAdditionalParameters(RestRequest request)
        {
            foreach (KeyValuePair<string, string> entry in this.AdditionalParameters)
            {
                request.AddParameter(entry.Key, entry.Value);
            }
        }

        /// <summary>
        /// Execute an API call using RestSharp and deserialize the response
        /// into a native object of class T.
        /// </summary>
        /// <typeparam name="T">The class to deserialize the response into.</typeparam>
        /// <param name="request">The RestRequest object to execute.</param>
        /// <returns></returns>
        private T Execute<T>(RestRequest request) where T : new()
        {
            InjectAdditionalParameters(request);
            var response = client.Execute<T>(request);
            HandleErrors(response);
            return response.Data;
        }

        private ObjectList<T> ExecuteList<T>(RestRequest request, string arrayKey) where T : new()
        {
            InjectAdditionalParameters(request);
            var response = client.Execute(request);
            HandleErrors(response);

            deserializer.RootElement = "list_info";
            var list = deserializer.Deserialize<ObjectList<T>>(response);

            // TODO: Check response sanity
            deserializer.RootElement = arrayKey;
            var items = deserializer.Deserialize<List<T>>(response);
            list.Items = items;

            return list;
        }

        /// <summary>
        /// Execute an API call and return nothing.
        /// </summary>
        /// <param name="request">The RestRequest object to execute.</param>
        /// <returns>The IRestResponse object.</returns>
        private IRestResponse Execute(RestRequest request)
        {
            InjectAdditionalParameters(request);
            var response = client.Execute(request);
            HandleErrors(response);
            return response;
        }

        /// <summary>
        /// Set the host of the HelloSign API to make calls to.
        /// Useful only for internal testing purposes; Users should generally not change this.
        /// </summary>
        /// <param name="host"></param>
        public void SetApiHost(string host)
        {
            client.BaseUrl = new Uri(String.Format("https://{0}/v3", host));
        }

        /// <summary>
        /// Set the client to point to a different environment.
        /// Not useful to the general public.
        /// </summary>
        /// <param name="env"></param>
        public void SetEnvironment(Environment env)
        {
            string domain;
            switch (env)
            {
                case Environment.Prod:
                    domain = "hellosign.com";
                    break;
                case Environment.QA:
                    domain = "qa-hellosign.com";
                    break;
                case Environment.Staging:
                    domain = "staging-hellosign.com";
                    break;
                case Environment.Dev:
                    domain = "dev-hellosign.com";
                    System.Net.ServicePointManager.ServerCertificateValidationCallback +=
                        delegate (object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                                System.Security.Cryptography.X509Certificates.X509Chain chain,
                                System.Net.Security.SslPolicyErrors sslPolicyErrors)
                        {
                            return true; // **** Always accept
                        };
                    break;
                default:
                    throw new ArgumentException("Unsupported environment given");
            }
            client.BaseUrl = new Uri(String.Format("https://api.{0}/v3", domain));
        }

        /// <summary>
        /// Throw an exception if credentials have not been supplied.
        /// </summary>
        private void RequireAuthentication()
        {
            if (client.Authenticator == null)
            {
                throw new UnauthorizedAccessException("This method requires authentication");
            }
        }

        #region Event Handling Helpers

        public Event ParseEvent(string data)
        {
            // Check for API key
            if (String.IsNullOrEmpty(apiKey))
            {
                throw new Exception("Event parsing is only supported if you initialize Client with an API key.");
            }

            // Build a fake RestResponse so we can take advantage of the RestSharp Deserializer
            var fakeResponse = new RestResponse();
            fakeResponse.Content = data;

            // Parse the main event body
            deserializer.RootElement = "event";
            var callbackEvent = deserializer.Deserialize<Event>(fakeResponse);
            
            // Verify hash integrity
            var hashInfo = deserializer.Deserialize<EventHashInfo>(fakeResponse);
            var keyBytes = System.Text.Encoding.ASCII.GetBytes(apiKey);
            var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
            var inputBytes = System.Text.Encoding.ASCII.GetBytes(hashInfo.EventTime + hashInfo.EventType);
            var outputBytes = hmac.ComputeHash(inputBytes);
            var hash = BitConverter.ToString(outputBytes).Replace("-", "").ToLower();

            // If no hash
            if (String.IsNullOrEmpty(hash))
            {
                throw new EventHashException("Was not able to compute event hash.", data);
            }

            // If mismatched hash
            if (hash != callbackEvent.EventHash)
            {
                throw new EventHashException("Event hash does not match expected value; This event may not be genuine. Make sure this API key matches the one on the account generating callbacks.", data);
            }

            // Parse attached models
            deserializer.RootElement = "signature_request";
            callbackEvent.SignatureRequest = deserializer.Deserialize<SignatureRequest>(fakeResponse);
            deserializer.RootElement = "template";
            callbackEvent.Template = deserializer.Deserialize<Template>(fakeResponse);
            deserializer.RootElement = "account";
            callbackEvent.Account = deserializer.Deserialize<Account>(fakeResponse);

            return callbackEvent;
        }

        #endregion

        #region Account Methods

        /// <summary>
        /// Create a new HelloSign account.
        /// The account must not already exist, and the user must confirm their
        /// email address before actions can be performed on this user's
        /// behalf.
        /// </summary>
        /// <param name="email_address"></param>
        /// <returns>The new Account</returns>
        public Account CreateAccount(string emailAddress)
        {
            if (String.IsNullOrWhiteSpace(emailAddress))
            {
                throw new ArgumentException("email_address is required");
            }

            var request = new RestRequest("account/create", Method.POST);
            request.AddParameter("email_address", emailAddress);
            request.RootElement = "account";
            return Execute<Account>(request);
        }

        /// <summary>
        /// Get information about the currently authenticated account.
        /// </summary>
        /// <returns>Your Account</returns>
        public Account GetAccount()
        {
            RequireAuthentication();

            var request = new RestRequest("account");
            request.RootElement = "account";
            return Execute<Account>(request);
        }

        /// <summary>
        /// Update your account settings (callback URL).
        /// </summary>
        /// <param name="callback_url">Your new account callback URL.</param>
        /// <returns>Your Account</returns>
        public Account UpdateAccount(Uri callbackUrl)
        {
            RequireAuthentication();

            var request = new RestRequest("account", Method.POST);
            request.AddParameter("callback_url", callbackUrl);
            request.RootElement = "account";
            return Execute<Account>(request);
        }

        public Account VerifyAccount(string emailAddress)
        {
            if (String.IsNullOrWhiteSpace(emailAddress))
            {
                throw new ArgumentException("email_address is required");
            }

            var request = new RestRequest("account/verify", Method.POST);
            request.AddParameter("email_address", emailAddress);
            request.RootElement = "account";
            return Execute<Account>(request);
        }

        #endregion

        #region Signature Request Methods

        /// <summary>
        /// Get information about a Signature Request.
        /// </summary>
        /// <param name="signatureRequestId">The alphanumeric Signature Request ID (Document ID).</param>
        /// <returns>The Signature Request</returns>
        public SignatureRequest GetSignatureRequest(string signatureRequestId)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/{id}");
            request.AddUrlSegment("id", signatureRequestId);
            request.RootElement = "signature_request";
            return Execute<SignatureRequest>(request);
        }

        public ObjectList<SignatureRequest> ListSignatureRequests(int? page = null, int? pageSize = null)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/list");
            if (page != null)
            {
                request.AddParameter("page", page);
            }
            if (pageSize != null)
            {
                request.AddParameter("page_size", pageSize);
            }
            return ExecuteList<SignatureRequest>(request, "signature_requests");
        }

        /// <summary>
        /// Internal method for calling /signature_request/send or /signature_request/create_embedded
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="clientId">App Client ID if associated with an API App (required for Embedded)</param>
        /// <param name="isEmbedded">True if for embedded signing; false otherwise</param>
        /// <returns></returns>
        private SignatureRequest _PostSignatureRequest(SignatureRequest signatureRequest, string clientId = null, bool isEmbedded = false)
        {
            RequireAuthentication();

            // Setup request
            var endpoint = (isEmbedded) ? "signature_request/create_embedded" : "signature_request/send";
            var request = new RestRequest(endpoint, Method.POST);

            // Add simple parameters
            if (clientId != null) request.AddParameter("client_id", clientId);
            if (signatureRequest.Title != null) request.AddParameter("title", signatureRequest.Title);
            if (signatureRequest.Subject != null) request.AddParameter("subject", signatureRequest.Subject);
            if (signatureRequest.Message != null) request.AddParameter("message", signatureRequest.Message);
            if (signatureRequest.TestMode) request.AddParameter("test_mode", "1");
            if (signatureRequest.SigningRedirectUrl != null) request.AddParameter("signing_redirect_url", signatureRequest.SigningRedirectUrl);
            if (signatureRequest.UseTextTags) request.AddParameter("use_text_tags", "1");
            if (signatureRequest.HideTextTags) request.AddParameter("hide_text_tags", "1");
            if (signatureRequest.AllowDecline) request.AddParameter("allow_decline", "1");
            if (signatureRequest.SkipMeNow) request.AddParameter("skip_me_now", "1");
            if (signatureRequest.AllowReassign) request.AddParameter("allow_reassign", "1");
            if (signatureRequest.RequesterEmailAddress != null) request.AddParameter("requester_email_address", signatureRequest.RequesterEmailAddress);

            // Add Signers
            var i = 0;
            foreach (var signer in signatureRequest.Signers)
            {
                string prefix = String.Format("signers[{0}]", i);
                request.AddParameter(prefix + "[email_address]", signer.EmailAddress);
                request.AddParameter(prefix + "[name]", signer.Name);
                if (signer.Order != null) request.AddParameter(prefix + "[order]", signer.Order);
                if (signer.Pin != null) request.AddParameter(prefix + "[pin]", signer.Pin);
                if (signer.SmsPhoneNumber != null) request.AddParameter(prefix + "[sms_phone_number]", signer.SmsPhoneNumber);
                i++;
            }

            // Add CCs
            i = 0;
            foreach (var cc in signatureRequest.Ccs)
            {
                request.AddParameter(String.Format("cc_email_addresses[{0}]", i), cc);
                i++;
            }

            // Add Files/FileUrls
            if (signatureRequest.Files.Count > 0)
            {
                i = 0;
                foreach (var file in signatureRequest.Files)
                {
                    file.AddToRequest(request, String.Format("file[{0}]", i));
                    i++;
                }
            }
            else if (signatureRequest.FileUrls.Count > 0)
            {
                i = 0;
                foreach (var fileUrl in signatureRequest.FileUrls)
                {
                    request.AddParameter(String.Format("file_url[{0}]", i), fileUrl);
                    i++;
                }
            }

            // Add Metadata
            foreach (var entry in signatureRequest.Metadata)
            {
                request.AddParameter(String.Format("metadata[{0}]", entry.Key), entry.Value); // TODO: Escape characters in key
            }

            // Add Signing Options
            if (signatureRequest.SigningOptions != null)
            {
                // Serialize as JSON string
                request.AddParameter("signing_options", JsonConvert.SerializeObject(signatureRequest.SigningOptions));
            }

            // Add form fields
            if (signatureRequest.FormFieldsPerDocument != null && signatureRequest.FormFieldsPerDocument.Any())
            {
                var container = signatureRequest.Files.Select(x => new List<FormField>()).ToArray();
                foreach (var fileFields in signatureRequest.FormFieldsPerDocument.GroupBy(x => x.file))
                {
                    container[fileFields.Key] = fileFields.ToList();
                }
                request.AddParameter("form_fields_per_document", JsonConvert.SerializeObject(container));
            }

            request.RootElement = "signature_request";
            return Execute<SignatureRequest>(request);
        }

        /// <summary>
        /// Create a new file-based Signature Request (NOT for Embedded Signing).
        ///
        /// Create a new SignatureRequest object, set its properties, and pass
        /// it to this method.
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="clientId">Client ID, if associated with an API App</param>
        /// <returns></returns>
        public SignatureRequest SendSignatureRequest(SignatureRequest signatureRequest, string clientId = null)
        {
            return _PostSignatureRequest(signatureRequest, clientId, false);
        }

        /// <summary>
        /// Create a new file-based Signature Request for Embedded Signing.
        ///
        /// Create a new SignatureRequest object, set its properties, and pass
        /// it to this method.
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="clientId">API App's Client ID</param>
        /// <returns></returns>
        public SignatureRequest CreateEmbeddedSignatureRequest(SignatureRequest signatureRequest, string clientId)
        {
            return _PostSignatureRequest(signatureRequest, clientId, true);
        }

        /// <summary>
        /// Internal method for calling /signature_request/send_with_template or
        /// /signature_request/create_embedded_with_template
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="clientId">App Client ID if associated with an API App (required for Embedded)</param>
        /// <param name="isEmbedded">True if for embedded signing; false otherwise</param>
        /// <returns></returns>
        private TemplateSignatureRequest _PostSignatureRequest(TemplateSignatureRequest signatureRequest, string clientId = null, bool isEmbedded = false)
        {
            RequireAuthentication();

            // Setup request
            var endpoint = (isEmbedded) ? "signature_request/create_embedded_with_template" : "signature_request/send_with_template";
            var request = new RestRequest(endpoint, Method.POST);

            // Add simple parameters
            if (clientId != null) request.AddParameter("client_id", clientId);
            if (signatureRequest.Title != null) request.AddParameter("title", signatureRequest.Title);
            if (signatureRequest.Subject != null) request.AddParameter("subject", signatureRequest.Subject);
            if (signatureRequest.Message != null) request.AddParameter("message", signatureRequest.Message);
            if (signatureRequest.SigningRedirectUrl != null) request.AddParameter("signing_redirect_url", signatureRequest.SigningRedirectUrl);
            if (signatureRequest.TestMode) request.AddParameter("test_mode", "1");
            if (signatureRequest.AllowDecline) request.AddParameter("allow_decline", "1");
            if (signatureRequest.SkipMeNow) request.AddParameter("skip_me_now", "1");
            if (signatureRequest.RequesterEmailAddress != null) request.AddParameter("requester_email_address", signatureRequest.RequesterEmailAddress);

            // Add Template IDs
            var i = 0;
            foreach (var templateId in signatureRequest.TemplateIds)
            {
                request.AddParameter(String.Format("template_ids[{0}]", i), templateId);
                i++;
            }

            // Add Signers
            foreach (var signer in signatureRequest.Signers)
            {
                string prefix = String.Format("signers[{0}]", signer.Role); // TODO: Escape characters in key
                request.AddParameter(prefix + "[email_address]", signer.EmailAddress);
                request.AddParameter(prefix + "[name]", signer.Name);
                if (signer.Order != null) request.AddParameter(prefix + "[order]", signer.Order);
                if (signer.Pin != null) request.AddParameter(prefix + "[pin]", signer.Pin);
                if (signer.SmsPhoneNumber != null) request.AddParameter(prefix + "[sms_phone_number]", signer.SmsPhoneNumber);
            }

            // Add CCs
            foreach (var entry in signatureRequest.Ccs)
            {
                request.AddParameter(String.Format("ccs[{0}][email_address]", entry.Key), entry.Value); // TODO: Escape characters in key
            }

            // Add Custom Fields
            if (signatureRequest.CustomFields.Count > 0)
            {
                // Serialize as JSON string
                request.AddParameter("custom_fields", JsonConvert.SerializeObject(signatureRequest.CustomFields));
            }

            // Add Metadata
            foreach (var entry in signatureRequest.Metadata)
            {
                request.AddParameter(String.Format("metadata[{0}]", entry.Key), entry.Value); // TODO: Escape characters in key
            }

            // Add Signing Options
            if (signatureRequest.SigningOptions != null)
            {
                // Serialize as JSON string
                request.AddParameter("signing_options", JsonConvert.SerializeObject(signatureRequest.SigningOptions));
            }

            request.RootElement = "signature_request";
            return Execute<TemplateSignatureRequest>(request);
        }

        /// <summary>
        /// Send a new Signature Request based on a Template.
        ///
        /// Create a new TemplateSignatureRequest object, set its properties,
        /// and pass it to this method.
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="clientId">Client ID, if associated with an API App</param>
        /// <returns></returns>
        public TemplateSignatureRequest SendSignatureRequest(TemplateSignatureRequest signatureRequest, string clientId = null)
        {
            return _PostSignatureRequest(signatureRequest, clientId, false);
        }

        /// <summary>
        /// Send a new Signature Request for Embedded Signing based on a Template.
        ///
        /// Create a new TemplateSignatureRequest object, set its properties,
        /// and pass it to this method.
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="clientId">API App's Client ID</param>
        /// <returns></returns>
        public TemplateSignatureRequest CreateEmbeddedSignatureRequest(TemplateSignatureRequest signatureRequest, string clientId)
        {
            return _PostSignatureRequest(signatureRequest, clientId, true);
        }

        /// <summary>
        /// Send a reminder to the specified email address to sign the
        /// specified Signature Request.
        /// </summary>
        /// <param name="signatureRequestId">The id of the SignatureRequest to send a reminder for.</param>
        /// <param name="emailAddress">The email address of the signer to send a reminder to.</param>
        /// <param name="name">Include if two or more signers share an email address.</param>
        public void RemindSignatureRequest(string signatureRequestId, string emailAddress, string name = null)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/remind/{id}", Method.POST);
            request.AddUrlSegment("id", signatureRequestId);
            request.AddParameter("email_address", emailAddress);
            if (name != null) {
                request.AddParameter("name", name);
            }
            Execute(request);
        }

        /// <summary>
        /// Update the email address for the specified signature ID on the
        /// specified Signature Request.
        /// </summary>
        /// <param name="signatureRequestId">The ID of the SignatureRequest to update.</param>
        /// <param name="signatureId">The signature ID for the recipient.</param>
        /// <param name="emailAddress">The new email address for the recipient.</param>
        /// <returns></returns>
        public SignatureRequest UpdateSignatureRequest(string signatureRequestId, string signatureId, string emailAddress)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/update/{id}", Method.POST);
            request.AddUrlSegment("id", signatureRequestId);
            request.AddParameter("signature_id", signatureId);
            request.AddParameter("email_address", emailAddress);
            request.RootElement = "signature_request";
            return Execute<SignatureRequest>(request);
        }

        /// <summary>
        /// Cancel the specified Signature Request.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        public void CancelSignatureRequest(string signatureRequestId)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/cancel/{id}", Method.POST);
            request.AddUrlSegment("id", signatureRequestId);
            Execute(request);
        }

        /// <summary>
        /// Remove the specified Signature Request from this account.
        ///
        /// Note: The Signature Request will still be available to other participants.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        public void RemoveSignatureRequest(string signatureRequestId)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/remove/{id}", Method.POST);
            request.AddUrlSegment("id", signatureRequestId);
            Execute(request);
        }

        /// <summary>
        /// Download a Signature Request as a merged PDF (or a ZIP of unmerged
        /// PDFs) and get the byte array.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public byte[] DownloadSignatureRequestFiles(string signatureRequestId, SignatureRequest.FileType type = SignatureRequest.FileType.PDF)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/files/{id}");
            request.AddUrlSegment("id", signatureRequestId);
            if (type == SignatureRequest.FileType.ZIP)
            {
                request.AddQueryParameter("file_type", "zip");
            }
            var response = Execute(request);
            return response.RawBytes;
        }

        /// <summary>
        /// Download a Signature Request as a merged PDF (or a ZIP of unmerged
        /// PDFs) and write the resulting file to disk.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public void DownloadSignatureRequestFiles(string signatureRequestId, string destination, SignatureRequest.FileType type = SignatureRequest.FileType.PDF)
        {
            File.WriteAllBytes(destination, DownloadSignatureRequestFiles(signatureRequestId, type));
        }

        /// <summary>
        /// Get a URL pointing to a downloadable PDF of the Signature Request.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        /// <returns>Information about a temporary URL which can be used to download the PDF.</returns>
        public TemporaryUrl GetSignatureRequestDownloadUrl(string signatureRequestId)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/files/{id}");
            request.AddUrlSegment("id", signatureRequestId);
            request.AddQueryParameter("get_url", "1");

            return Execute<TemporaryUrl>(request);
        }

        /// <summary>
        /// Release the specified Signature Request.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        /// <returns></returns>
        public SignatureRequest ReleaseSignatureRequest(string signatureRequestId)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/release_hold/{id}", Method.POST);
            request.AddUrlSegment("id", signatureRequestId);
            return Execute<SignatureRequest>(request);
        }

        #endregion

        #region Template Methods

        /// <summary>
        /// Get information about a Template.
        /// </summary>
        /// <param name="templateId">The alphanumeric Template ID.</param>
        /// <returns>The Template</returns>
        public Template GetTemplate(string templateId)
        {
            RequireAuthentication();

            var request = new RestRequest("template/{id}");
            request.AddUrlSegment("id", templateId);
            request.RootElement = "template";
            return Execute<Template>(request);
        }

        public ObjectList<Template> ListTemplates(int? page = null, int? pageSize = null)
        {
            RequireAuthentication();

            var request = new RestRequest("template/list");
            if (page != null)
            {
                request.AddParameter("page", page);
            }
            if (pageSize != null)
            {
                request.AddParameter("page_size", pageSize);
            }
            return ExecuteList<Template>(request, "templates");
        }

        /// <summary>
        /// Internal method for issuing add_user or remove_user calls for templates.
        /// </summary>
        /// <param name="templateId">The template ID.</param>
        /// <param name="isGrant">True if granting, false if revoking.</param>
        /// <param name="isEmailAddress">True if identifier is an email address, false if it's a GUID.</param>
        /// <param name="identifier">The email address or GUID.</param>
        /// <returns></returns>
        private Template _ModifyTemplatePermission(string templateId, bool isGrant, string accountId, string emailAddress)
        {
            RequireAuthentication();

            if ((accountId != null) && (emailAddress != null))
            {
                throw new ArgumentException("Specify accountId OR emailAddress, but not both");
            }

            var request = new RestRequest("template/{action}_user/{id}", Method.POST);
            request.AddUrlSegment("action", (isGrant) ? "add" : "remove");
            request.AddUrlSegment("id", templateId);
            if (accountId != null)
                request.AddParameter("account_id", accountId);
            else if (emailAddress != null)
                request.AddParameter("email_address", emailAddress);
            else
                throw new ArgumentException("accountId or emailAddress is required");
            request.RootElement = "template";
            return Execute<Template>(request);
        }

        /// <summary>
        /// Grant an account access to an existing template.
        ///
        /// Specify a value for either accountId OR emailAddress (not both).
        /// </summary>
        /// <param name="templateId"></param>
        /// <param name="accountId"></param>
        /// <param name="emailAddress"></param>
        /// <returns></returns>
        public Template AddAccountToTemplate(string templateId, string accountId = null, string emailAddress = null)
        {
            return _ModifyTemplatePermission(templateId, true, accountId, emailAddress);
        }

        /// <summary>
        /// Revoke access to an existing template from an account.
        ///
        /// Specify a value for either accountId OR emailAddress (not both).
        /// </summary>
        /// <param name="templateId"></param>
        /// <param name="accountId"></param>
        /// <param name="emailAddress"></param>
        /// <returns></returns>
        public Template RemoveAccountFromTemplate(string templateId, string accountId = null, string emailAddress = null)
        {
            return _ModifyTemplatePermission(templateId, false, accountId, emailAddress);
        }

        public EmbeddedTemplate CreateEmbeddedTemplateDraft(EmbeddedTemplateDraft draft, string clientId)
        {
            RequireAuthentication();

            // Set up request
            var request = new RestRequest("template/create_embedded_draft", Method.POST);

            // Add simple parameters
            if (clientId != null) request.AddParameter("client_id", clientId);
            if (draft.Title != null) request.AddParameter("title", draft.Title);
            if (draft.Subject != null) request.AddParameter("subject", draft.Subject);
            if (draft.Message != null) request.AddParameter("message", draft.Message);
            if (draft.TestMode) request.AddParameter("test_mode", "1");
            if (draft.SkipMeNow) request.AddParameter("skip_me_now", "1");
            if (draft.UsePreexistingFields) request.AddParameter("use_preexisting_fields", "1");

            // Add Signer Roles
            var i = 0;
            foreach (var role in draft.SignerRoles)
            {
                string prefix = String.Format("signer_roles[{0}]", i);
                request.AddParameter(prefix + "[name]", role.Name);
                if (role.Order != null) request.AddParameter(prefix + "[order]", role.Order);
                i++;
            }

            // Add CC Roles
            i = 0;
            foreach (var cc in draft.Ccs)
            {
                request.AddParameter(String.Format("cc_roles[{0}]", i), cc);
                i++;
            }

            // Add Files/FileUrls
            if (draft.Files.Count > 0)
            {
                i = 0;
                foreach (var file in draft.Files)
                {
                    file.AddToRequest(request, String.Format("file[{0}]", i));
                    i++;
                }
            }
            else if (draft.FileUrls.Count > 0)
            {
                i = 0;
                foreach (var fileUrl in draft.FileUrls)
                {
                    request.AddParameter(String.Format("file_url[{0}]", i), fileUrl);
                    i++;
                }
            }

            // Add Metadata
            foreach (var entry in draft.Metadata)
            {
                request.AddParameter(String.Format("metadata[{0}]", entry.Key), entry.Value); // TODO: Escape characters in key
            }

            // Add Merge Fields (JSON)
            if (draft.MergeFields.Count > 0)
            {
                request.AddParameter("merge_fields", JsonConvert.SerializeObject(draft.MergeFields));
            }

            request.RootElement = "template";
            return Execute<EmbeddedTemplate>(request);
        }

        /// <summary>
        /// Delete a Template.
        /// </summary>
        /// <param name="templateId">The alphanumeric Template ID.</param>
        public void DeleteTemplate(string templateId)
        {
            RequireAuthentication();

            var request = new RestRequest("template/delete/{id}", Method.POST);
            request.AddUrlSegment("id", templateId);
            Execute(request);
        }

        /// <summary>
        /// Download a Template's files as a merged PDF (or a ZIP of unmerged
        /// PDFs) and get the byte array.
        /// </summary>
        /// <param name="templateId"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public byte[] DownloadTemplateFiles(string templateId, SignatureRequest.FileType type = SignatureRequest.FileType.PDF)
        {
            RequireAuthentication();

            var request = new RestRequest("template/files/{id}");
            request.AddUrlSegment("id", templateId);
            if (type == SignatureRequest.FileType.ZIP)
            {
                request.AddQueryParameter("file_type", "zip");
            }
            var response = Execute(request);
            return response.RawBytes;
        }

        /// <summary>
        /// Download a Template's files as a merged PDF (or a ZIP of unmerged
        /// PDFs) and write the resulting file to disk.
        /// </summary>
        /// <param name="templateId"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public void DownloadTemplateFiles(string templateId, string destination, SignatureRequest.FileType type = SignatureRequest.FileType.PDF)
        {
            File.WriteAllBytes(destination, DownloadTemplateFiles(templateId, type));
        }

        /// <summary>
        /// Get a URL pointing to a downloadable PDF of the Signature Request.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        /// <returns>Information about a temporary URL which can be used to download the PDF.</returns>
        public TemporaryUrl GetTemplateFilesDownloadUrl(string templateId)
        {
            RequireAuthentication();

            var request = new RestRequest("template/files/{id}");
            request.AddUrlSegment("id", templateId);
            request.AddQueryParameter("get_url", "1");

            return Execute<TemporaryUrl>(request);
        }

        #endregion

        #region Team Methods

        /// <summary>
        /// Get information about your current team.
        /// </summary>
        /// <returns>The Team object</returns>
        public Team GetTeam()
        {
            RequireAuthentication();

            var request = new RestRequest("team");
            request.RootElement = "team";
            return Execute<Team>(request);
        }

        /// <summary>
        /// Create a new team.
        ///
        /// Will fail if you are already on a team.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Team CreateTeam(string name)
        {
            RequireAuthentication();

            var request = new RestRequest("team/create", Method.POST);
            request.AddParameter("name", name);
            request.RootElement = "team";
            return Execute<Team>(request);
        }

        /// <summary>
        /// Update the name of your current team.
        /// </summary>
        /// <param name="name">The new name.</param>
        /// <returns></returns>
        public Team UpdateTeamName(string name)
        {
            RequireAuthentication();

            var request = new RestRequest("team");
            request.AddParameter("name", name);
            request.RootElement = "team";
            return Execute<Team>(request);
        }

        /// <summary>
        /// Destroy your team.
        /// </summary>
        public void DeleteTeam()
        {
            RequireAuthentication();

            var request = new RestRequest("team/destroy", Method.POST);
            Execute(request);
        }

        /// <summary>
        /// Internal method for adding/removing someone from your team.
        /// </summary>
        /// <param name="isAdd">True if adding, false if removing.</param>
        /// <param name="accountId"></param>
        /// <param name="emailAddress"></param>
        /// <returns></returns>
        private Team _ModifyTeamMembership(bool isAdd, string accountId, string emailAddress)
        {
            RequireAuthentication();

            if ((accountId != null) && (emailAddress != null))
            {
                throw new ArgumentException("Specify accountId OR emailAddress, but not both");
            }

            var request = new RestRequest("team/{action}_member", Method.POST);
            request.AddUrlSegment("action", (isAdd) ? "add" : "remove");
            if (accountId != null)
                request.AddParameter("account_id", accountId);
            else if (emailAddress != null)
                request.AddParameter("email_address", emailAddress);
            else
                throw new ArgumentException("accountId or emailAddress is required");
            request.RootElement = "team";
            return Execute<Team>(request);
        }

        /// <summary>
        /// Add a member to your team.
        ///
        /// Specify a value for either accountId OR emailAddress (not both).
        /// </summary>
        /// <param name="accountId"></param>
        /// <returns></returns>
        public Team AddMemberToTeam(string accountId = null, string emailAddress = null)
        {
            return _ModifyTeamMembership(true, accountId, emailAddress);
        }

        /// <summary>
        /// Remove a member from your team.
        ///
        /// Specify a value for either accountId OR emailAddress (not both).
        /// </summary>
        /// <param name="accountId"></param>
        /// <returns></returns>
        public Team RemoveMemberFromTeam(string accountId = null, string emailAddress = null)
        {
            return _ModifyTeamMembership(false, accountId, emailAddress);
        }

        #endregion

        #region Unclaimed Draft Methods
        
        /// <summary>
        /// Internal method that handles unclaimed_draft/create[_embedded].
        /// </summary>
        private UnclaimedDraft _CreateUnclaimedDraft(SignatureRequest signatureRequest, UnclaimedDraft.Type? type, string clientId)
        {
            RequireAuthentication();
            
            // Determine embedded/non-embedded
            bool embedded;
            if (!String.IsNullOrEmpty(clientId))
            {
                embedded = true;
            }
            else if (type != null)
            {
                embedded = false;
            }
            else
            {
                throw new ArgumentException("Invalid arguments provided to _CreateUnclaimedDraft");
            }

            // Setup request
            var endpoint = (embedded) ? "unclaimed_draft/create_embedded" : "unclaimed_draft/create";
            var request = new RestRequest(endpoint, Method.POST);

            if (embedded)
            {
                request.AddParameter("client_id", clientId);
            }
            else
            {
                string typeString;
                switch (type)
                {
                    case UnclaimedDraft.Type.SendDocument:
                        typeString = "send_document";
                        break;
                    case UnclaimedDraft.Type.RequestSignature:
                        typeString = "request_signature";
                        break;
                    default:
                        throw new ArgumentException("Unsupported type specified");
                }
                request.AddParameter("type", typeString);
            }

            // Add simple parameters
            if (signatureRequest.Title != null) request.AddParameter("title", signatureRequest.Title);
            if (signatureRequest.Subject != null) request.AddParameter("subject", signatureRequest.Subject);
            if (signatureRequest.Message != null) request.AddParameter("message", signatureRequest.Message);
            if (signatureRequest.TestMode) request.AddParameter("test_mode", "1");
            if (signatureRequest.UseTextTags) request.AddParameter("use_text_tags", "1");
            if (signatureRequest.UsePreexistingFields) request.AddParameter("use_preexisting_fields", "1");
            if (signatureRequest.HideTextTags) request.AddParameter("hide_text_tags", "1");
            if (signatureRequest.AllowDecline) request.AddParameter("allow_decline", "1");
            if (signatureRequest.SkipMeNow) request.AddParameter("skip_me_now", "1");
            if (signatureRequest.HoldRequest) request.AddParameter("hold_request", "1");
            if (embedded && signatureRequest.IsForEmbeddedSigning) request.AddParameter("is_for_embedded_signing", "1");
            if (signatureRequest.RequesterEmailAddress != null) request.AddParameter("requester_email_address", signatureRequest.RequesterEmailAddress);

            // Add Signers
            var i = 0;
            foreach (var signer in signatureRequest.Signers)
            {
                string prefix = String.Format("signers[{0}]", i);
                request.AddParameter(prefix + "[email_address]", signer.EmailAddress);
                request.AddParameter(prefix + "[name]", signer.Name);
                if (signer.Order != null) request.AddParameter(prefix + "[order]", signer.Order);
                if (signer.Pin != null) request.AddParameter(prefix + "[pin]", signer.Pin);
                i++;
            }

            // Add CCs
            i = 0;
            foreach (var cc in signatureRequest.Ccs)
            {
                request.AddParameter(String.Format("cc_email_addresses[{0}]", i), cc);
                i++;
            }

            // Add Files/FileUrls
            if (signatureRequest.Files.Count > 0)
            {
                i = 0;
                foreach (var file in signatureRequest.Files)
                {
                    file.AddToRequest(request, String.Format("file[{0}]", i));
                    i++;
                }
            }
            else if (signatureRequest.FileUrls.Count > 0)
            {
                i = 0;
                foreach (var fileUrl in signatureRequest.FileUrls)
                {
                    request.AddParameter(String.Format("file_url[{0}]", i), fileUrl);
                    i++;
                }
            }

            // Add Metadata
            foreach (var entry in signatureRequest.Metadata)
            {
                request.AddParameter(String.Format("metadata[{0}]", entry.Key), entry.Value); // TODO: Escape characters in key
            }

            // Add Signing Options
            if (signatureRequest.SigningOptions != null)
            {
                // Serialize as JSON string
                request.AddParameter("signing_options", JsonConvert.SerializeObject(signatureRequest.SigningOptions));
            }

            // TODO: Form fields per doc

            request.RootElement = "unclaimed_draft";
            return Execute<UnclaimedDraft>(request);
        }

        /// <summary>
        /// Create a non-embedded unclaimed draft.
        /// </summary>
        public UnclaimedDraft CreateUnclaimedDraft(SignatureRequest signatureRequest, UnclaimedDraft.Type type)
        {
            return _CreateUnclaimedDraft(signatureRequest, type, null);
        }
        
        /// <summary>
        /// Create an embedded unclaimed draft (for embedded requesting).
        /// </summary>
        public UnclaimedDraft CreateUnclaimedDraft(SignatureRequest signatureRequest, string clientId = null)
        {
            return _CreateUnclaimedDraft(signatureRequest, null, clientId);
        }

        public UnclaimedDraft CreateUnclaimedDraft(TemplateSignatureRequest signatureRequest, string clientId)
        {
            RequireAuthentication();

            // Setup request
            var endpoint = "unclaimed_draft/create_embedded_with_template";
            var request = new RestRequest(endpoint, Method.POST);

            // Add simple parameters
            request.AddParameter("client_id", clientId);
            if (signatureRequest.Title != null) request.AddParameter("title", signatureRequest.Title);
            if (signatureRequest.Subject != null) request.AddParameter("subject", signatureRequest.Subject);
            if (signatureRequest.Message != null) request.AddParameter("message", signatureRequest.Message);
            if (signatureRequest.SigningRedirectUrl != null) request.AddParameter("signing_redirect_url", signatureRequest.SigningRedirectUrl);
            if (signatureRequest.RequestingRedirectUrl != null) request.AddParameter("requesting_redirect_url", signatureRequest.RequestingRedirectUrl);
            if (signatureRequest.TestMode) request.AddParameter("test_mode", "1");
            if (signatureRequest.IsForEmbeddedSigning) request.AddParameter("is_for_embedded_signing", "1");
            if (signatureRequest.SkipMeNow) request.AddParameter("skip_me_now", "1");
            if (signatureRequest.RequesterEmailAddress != null) request.AddParameter("requester_email_address", signatureRequest.RequesterEmailAddress);

            // Add Template IDs
            var i = 0;
            foreach (var templateId in signatureRequest.TemplateIds)
            {
                request.AddParameter(String.Format("template_ids[{0}]", i), templateId);
                i++;
            }

            // Add Signers
            foreach (var signer in signatureRequest.Signers)
            {
                string prefix = String.Format("signers[{0}]", signer.Role); // TODO: Escape characters in key
                request.AddParameter(prefix + "[email_address]", signer.EmailAddress);
                request.AddParameter(prefix + "[name]", signer.Name);
                if (signer.Order != null) request.AddParameter(prefix + "[order]", signer.Order);
                if (signer.Pin != null) request.AddParameter(prefix + "[pin]", signer.Pin);
            }

            // Add CCs
            foreach (var entry in signatureRequest.Ccs)
            {
                request.AddParameter(String.Format("ccs[{0}][email_address]", entry.Key), entry.Value); // TODO: Escape characters in key
            }

            // Add Custom Fields
            if (signatureRequest.CustomFields.Count > 0)
            {
                // Serialize as JSON string
                request.AddParameter("custom_fields", JsonConvert.SerializeObject(signatureRequest.CustomFields));
            }

            // Add Metadata
            foreach (var entry in signatureRequest.Metadata)
            {
                request.AddParameter(String.Format("metadata[{0}]", entry.Key), entry.Value); // TODO: Escape characters in key
            }

            // Add Signing Options
            if (signatureRequest.SigningOptions != null)
            {
                // Serialize as JSON string
                request.AddParameter("signing_options", JsonConvert.SerializeObject(signatureRequest.SigningOptions));
            }

            request.RootElement = "unclaimed_draft";
            return Execute<UnclaimedDraft>(request);
        }

        #endregion

        #region Embedded Methods

        /// <summary>
        /// Retrieve an embedded object containing a signature url that can be
        /// opened in an iFrame.
        /// </summary>
        /// <param name="signatureId"></param>
        /// <returns></returns>
        public EmbeddedSign GetSignUrl(string signatureId)
        {
            RequireAuthentication();

            var request = new RestRequest("embedded/sign_url/{id}");
            request.AddUrlSegment("id", signatureId);
            request.RootElement = "embedded";
            return Execute<EmbeddedSign>(request);
        }

        /// <summary>
        /// Retrieve an embedded object containing a template url that can be
        /// opened in an iFrame. Note that only templates created via the
        /// embedded template process are available to be edited with this
        /// endpoint.
        /// </summary>
        /// <param name="templateId"></param>
        /// <param name="skipSignerRoles">If signer roles were already provided, do not prompt the user to edit them.</param>
        /// <param name="skipSubjectMessage">If subject/message were already provided, do not prompt the user to edit them.</param>
        /// <returns></returns>
        public EmbeddedTemplate GetEditUrl(string templateId, bool skipSignerRoles = false, bool skipSubjectMessage = false, bool testMode = false)
        {
            RequireAuthentication();

            var request = new RestRequest("embedded/edit_url/{id}");
            request.AddUrlSegment("id", templateId);
            if (skipSignerRoles) request.AddQueryParameter("skip_signer_roles", "1");
            if (skipSubjectMessage) request.AddQueryParameter("skip_subject_message", "1");
            if (testMode) request.AddQueryParameter("test_mode", "1");
            request.RootElement = "embedded";
            return Execute<EmbeddedTemplate>(request);
        }

        #endregion
        
        #region API App Methods
        
        /// <summary>
        /// Get information about an API App.
        /// </summary>
        /// <param name="clientId">The app's client ID.</param>
        /// <returns>The API App</returns>
        public ApiApp GetApiApp(string clientId)
        {
            RequireAuthentication();

            var request = new RestRequest("api_app/{id}");
            request.AddUrlSegment("id", clientId);
            request.RootElement = "api_app";
            return Execute<ApiApp>(request);
        }

        public ObjectList<ApiApp> ListApiApps(int? page = null, int? pageSize = null)
        {
            RequireAuthentication();

            var request = new RestRequest("api_app/list");
            if (page != null)
            {
                request.AddParameter("page", page);
            }
            if (pageSize != null)
            {
                request.AddParameter("page_size", pageSize);
            }
            return ExecuteList<ApiApp>(request, "api_apps");
        }

        /// <summary>
        /// Create a new API App.
        /// </summary>
        /// <param name="app">An ApiApp object with the desired values set.</param>
        /// <returns>The new API App (given back by the server)</returns>
        public ApiApp CreateApiApp(ApiApp app)
        {
            RequireAuthentication();

            var request = new RestRequest("api_app", Method.POST);

            // Add simple parameters
            request.AddParameter("name", app.Name);
            request.AddParameter("domain", app.Domain);
            if (app.CallbackUrl != null) request.AddParameter("callback_url", app.CallbackUrl);

            // Add OAuth info if present
            if (app.Oauth != null)
            {
                request.AddParameter("oauth[callback_url]", app.Oauth.CallbackUrl);
                request.AddParameter("oauth[scopes]", String.Join(",", app.Oauth.Scopes.ToArray()));
            }

            request.RootElement = "api_app";
            return Execute<ApiApp>(request);
        }
        
        /// <summary>
        /// Updates an existing API App's white_labeling_options.
        /// </summary>
        /// <param name="clientId">The app's client ID.</param>
        /// <param name="whiteLabel">The app's client ID.</param>
        /// <returns>The new API App (given back by the server)</returns>
        public ApiApp UpdateApiApp(string clientId, WhiteLabel whiteLabel)
        {
            RequireAuthentication();
            
            var request = new RestRequest("api_app/{id}", Method.POST);
            request.AddUrlSegment("id", clientId);

            // Add simple parameters
            var whiteLabelingOptions = JsonConvert.SerializeObject(whiteLabel);
            request.AddParameter("white_labeling_options", whiteLabelingOptions);

            request.RootElement = "api_app";
            return Execute<ApiApp>(request);
        }
        
        /// <summary>
        /// Delete an API App.
        /// </summary>
        /// <param name="clientId">The app's client ID.</param>
        public void DeleteApiApp(string clientId)
        {
            RequireAuthentication();

            var request = new RestRequest("api_app/{id}", Method.DELETE);
            request.AddUrlSegment("id", clientId);
            Execute(request);
        }

        #endregion

        #region Bulk Send Job Methods

        /// <summary>
        /// Get a single Bulk Send Job, including a page of its Signature Requests.
        /// </summary>
        public BulkSendJob GetBulkSendJob(string bulkSendJobId, int? page = null, int? pageSize = null)
        {
            RequireAuthentication();

            var request = new RestRequest("bulk_send_job/{id}");
            request.AddUrlSegment("id", bulkSendJobId);
            request.RootElement = "bulk_send_job";

            // Special twist on ExecuteList
            InjectAdditionalParameters(request);
            var response = client.Execute(request);
            HandleErrors(response);

            // Unpack list_info
            deserializer.RootElement = "list_info";
            var job = deserializer.Deserialize<BulkSendJob>(response);

            // Unpack list of associated SignatureRequests
            deserializer.RootElement = "signature_requests";
            job.Items = deserializer.Deserialize<List<SignatureRequest>>(response);

            // Also unpack the BulkSendJobInfo details
            deserializer.RootElement = "bulk_send_job";
            job.JobInfo = deserializer.Deserialize<BulkSendJobInfo>(response);

            return job;
        }

        /// <inheritdoc />
        public BulkSendJob GetBulkSendJob(BulkSendJobInfo jobInfo, int? page = null, int? pageSize = null)
        {
            return this.GetBulkSendJob(jobInfo.BulkSendJobId);
        }

        /// <summary>
        /// Get a list of Bulk Send Jobs.
        ///
        /// Note that the items returned are of type BulkSendJobInfo, a
        /// lightweight structure containing information about the Job.
        /// If you wish to list Signature Requests for a given job, use
        /// the GetBulkSendJob method to fetch a single job at a time.
        /// </summary>
        public ObjectList<BulkSendJobInfo> ListBulkSendJobs(int? page = null, int? pageSize = null)
        {
            RequireAuthentication();

            var request = new RestRequest("bulk_send_job/list");
            if (page != null)
            {
                request.AddParameter("page", page);
            }
            if (pageSize != null)
            {
                request.AddParameter("page_size", pageSize);
            }
            return ExecuteList<BulkSendJobInfo>(request, "bulk_send_jobs");
        }

        #endregion

        #region Reports

        /// <summary>
        /// Request the creation of user activity and/or document status report(s) in defined time window
        /// </summary>
        /// <param name="startDate">The start date of the report data in MM/DD/YYYY format (inclusive date)</param>
        /// <param name="endDate">The end date of the report data in MM/DD/YYYY format (inclusive date)</param>
        /// <param name="reportTypes">The type of the report(s) you are requesting</param>
        /// <returns>The Report object</returns>
        public Report CreateReport(Report report)
        {
            RequireAuthentication();

            var request = new RestRequest("report/create", Method.POST);
            request.AddQueryParameter("start_date", report.StartDate.ToString("MM/dd/yyyy"));
            request.AddQueryParameter("end_date", report.EndDate.ToString("MM/dd/yyyy"));

            // Add Report Types
            var reportTypes = report.ReportType.Split(',');
            int i = 0;
            foreach (var reportType in reportTypes)
            {
                request.AddQueryParameter(String.Format("report_type[{0}]", i), reportType.Trim());
                i++;
            }

            request.RootElement = "report";
            return Execute<Report>(request);
        }
        #endregion
    }
}
