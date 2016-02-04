﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Diagnostics;
using RestSharp;

namespace HelloSign
{
    internal class Tools
    {
        /// <summary>
        /// UTC DateTime instance for the Unix time epoch (1970-1-1).
        /// </summary>
        public static DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        /// <summary>
        /// Returns a UTC DateTime instance from a Unix timestamp (in seconds).
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
        public List<Warning> Warnings { get; private set; }
        public string Version { get; private set; }

        /// <summary>
        /// Default constructor with no authentication.
        /// Limited to unauthenticated calls only.
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
            SetEnvironment(Environment.Prod);
        }

        /// <summary>
        /// Constructor initialized with API key authentication.
        /// </summary>
        /// <param name="apiKey">Your HelloSign account API key.</param>
        public Client(string apiKey) : this()
        {
            this.apiKey = apiKey;
            client.Authenticator = new HttpBasicAuthenticator(apiKey, "");
        }

        /// <summary>
        /// Constructor initialized with username/password authentication.
        /// Not preferred; Please use API key authentication instead.
        /// </summary>
        /// <param name="username">Your HelloSign account email address.</param>
        /// <param name="password">Your HelloSign account password.</param>
        public Client(string username, string password) : this()
        {
            client.Authenticator = new HttpBasicAuthenticator(username, password);
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

        /// <summary>
        /// Execute an API call using RestSharp and deserialize the response
        /// into a native object of class T.
        /// </summary>
        /// <typeparam name="T">The class to deserialize the response into.</typeparam>
        /// <param name="request">The RestRequest object to execute.</param>
        /// <returns></returns>
        private T Execute<T>(RestRequest request) where T : new()
        {
            var response = client.Execute<T>(request);
            HandleErrors(response);
            return response.Data;
        }

        private ObjectList<T> ExecuteList<T>(RestRequest request, string arrayKey) where T : new()
        {
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
            var response = client.Execute(request);
            HandleErrors(response);
            return response;
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
        /// <param name="clientId">App Client ID if for embedded signing; null otherwise</param>
        /// <returns></returns>
        private SignatureRequest _PostSignatureRequest(SignatureRequest signatureRequest, bool isEmbeddedRequest = false, string clientId = null)
        {
            RequireAuthentication();

            // Setup request
            var endpoint = isEmbeddedRequest && clientId != null ?"signature_request/create_embedded" : "signature_request/send";
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
        /// <param name="clientId"></param>
        /// <returns></returns>
        public SignatureRequest SendSignatureRequest(SignatureRequest signatureRequest, string clientId = null)
        {
            return _PostSignatureRequest(signatureRequest, false, clientId);
        }

        /// <summary>
        /// Create a new file-based Signature Request for Embedded Signing.
        ///
        /// Create a new SignatureRequest object, set its properties, and pass
        /// it to this method.
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <param name="clientId"></param>
        /// <returns></returns>
        public SignatureRequest CreateEmbeddedSignatureRequest(SignatureRequest signatureRequest, string clientId)
        {
            return _PostSignatureRequest(signatureRequest, true, clientId);
        }

        /// <summary>
        /// Internal method for calling /signature_request/send_with_template or
        /// /signature_request/create_embedded_with_template
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <returns></returns>
        public TemplateSignatureRequest _PostSignatureRequest(TemplateSignatureRequest signatureRequest, string clientId = null)
        {
            RequireAuthentication();

            // Setup request
            var endpoint = (clientId == null) ? "signature_request/send_with_template" : "signature_request/create_embedded_with_template";
            var request = new RestRequest(endpoint, Method.POST);

            // Add simple parameters
            request.AddParameter("template_id", signatureRequest.TemplateId);
            if (clientId != null) request.AddParameter("client_id", clientId);
            if (signatureRequest.Title != null) request.AddParameter("title", signatureRequest.Title);
            if (signatureRequest.Subject != null) request.AddParameter("subject", signatureRequest.Subject);
            if (signatureRequest.Message != null) request.AddParameter("message", signatureRequest.Message);
            if (signatureRequest.SigningRedirectUrl != null) request.AddParameter("signing_redirect_url", signatureRequest.SigningRedirectUrl);
            if (signatureRequest.TestMode) request.AddParameter("test_mode", "1");

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
            foreach (var entry in signatureRequest.CustomFields)
            {
                request.AddParameter(String.Format("custom_fields[{0}]", entry.Name), entry.Value); // TODO: Escape characters in key
            }

            // Add Metadata
            foreach (var entry in signatureRequest.Metadata)
            {
                request.AddParameter(String.Format("metadata[{0}]", entry.Key), entry.Value); // TODO: Escape characters in key
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
        /// <returns></returns>
        public TemplateSignatureRequest SendSignatureRequest(TemplateSignatureRequest signatureRequest)
        {
            return _PostSignatureRequest(signatureRequest);
        }

        /// <summary>
        /// Send a new Signature Request for Embedded Signing based on a Template.
        ///
        /// Create a new TemplateSignatureRequest object, set its properties,
        /// and pass it to this method.
        /// </summary>
        /// <param name="signatureRequest"></param>
        /// <returns></returns>
        public TemplateSignatureRequest CreateEmbeddedSignatureRequest(TemplateSignatureRequest signatureRequest, string clientId)
        {
            return _PostSignatureRequest(signatureRequest, clientId);
        }

        /// <summary>
        /// Send a reminder to the specified email address to sign the
        /// specified Signature Request.
        /// </summary>
        /// <param name="signatureRequestId"></param>
        /// <param name="emailAddress"></param>
        public void RemindSignatureRequest(string signatureRequestId, string emailAddress)
        {
            RequireAuthentication();

            var request = new RestRequest("signature_request/remind/{id}", Method.POST);
            request.AddUrlSegment("id", signatureRequestId);
            request.AddParameter("email_address", emailAddress);
            Execute(request);
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

            var request = new RestRequest("template/{action}_user/{id}");
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

//         public EmbeddedTemplate CreateEmbeddedTemplateDraft(EmbeddedTemplateDraft draft, string clientId)
//         {
//             RequireAuthentication();
// 
//             var request = new RestRequest("template/create_embedded_draft", Method.POST);
// 
//             // TODO!
// 
//             return Execute<EmbeddedTemplate>(request);
//         }

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

            var request = new RestRequest("team/{action}_member");
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
            if (embedded && signatureRequest.IsForEmbeddedSigning) request.AddParameter("is_for_embedded_signing", "1");
            if (embedded && signatureRequest.RequesterEmailAddress != null) request.AddParameter("requester_email_address", signatureRequest.RequesterEmailAddress);

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
            request.AddParameter("template_id", signatureRequest.TemplateId);
            request.AddParameter("client_id", clientId);
            if (signatureRequest.Title != null) request.AddParameter("title", signatureRequest.Title);
            if (signatureRequest.Subject != null) request.AddParameter("subject", signatureRequest.Subject);
            if (signatureRequest.Message != null) request.AddParameter("message", signatureRequest.Message);
            if (signatureRequest.SigningRedirectUrl != null) request.AddParameter("signing_redirect_url", signatureRequest.SigningRedirectUrl);
            if (signatureRequest.RequestingRedirectUrl != null) request.AddParameter("requesting_redirect_url", signatureRequest.RequestingRedirectUrl);
            if (signatureRequest.TestMode) request.AddParameter("test_mode", "1");
            if (signatureRequest.IsForEmbeddedSigning) request.AddParameter("is_for_embedded_signing", "1");
            if (signatureRequest.RequesterEmailAddress != null) request.AddParameter("requester_email_address", signatureRequest.RequesterEmailAddress);

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
            foreach (var entry in signatureRequest.CustomFields)
            {
                request.AddParameter(String.Format("custom_fields[{0}]", entry.Name), entry.Value); // TODO: Escape characters in key
            }

            // Add Metadata
            foreach (var entry in signatureRequest.Metadata)
            {
                request.AddParameter(String.Format("metadata[{0}]", entry.Key), entry.Value); // TODO: Escape characters in key
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
        public EmbeddedTemplate GetEditUrl(string templateId, bool skipSignerRoles = false, bool skipSubjectMessage = false)
        {
            RequireAuthentication();

            var request = new RestRequest("embedded/edit_url/{id}");
            request.AddUrlSegment("id", templateId);
            if (skipSignerRoles) request.AddQueryParameter("skip_signer_roles", "1");
            if (skipSubjectMessage) request.AddQueryParameter("skip_subject_message", "1");
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
            request.AddParameter("domain", app.Name);
            if (app.CallbackUrl != null) request.AddParameter("callback_url", app.CallbackUrl);

            // Add OAuth info if present
            if (app.Oauth != null)
            {
                request.AddParameter("oauth[callback_url]", app.Oauth.CallbackUrl);
                request.AddParameter("oauth[scopes]", String.Join(",", app.Oauth.Scopes));
            }

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
    }
}
