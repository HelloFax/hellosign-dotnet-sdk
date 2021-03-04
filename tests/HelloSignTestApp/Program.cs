using System;
using HelloSign;

namespace HelloSignTestApp
{
    class Program
    {
        // Configuration
        const string TEMPLATE_ID = ""; // Your test Template ID goes here (signer role "Client", CC role "Accounting", custom field "Cost")

        // Helper function for auto-retrying CancelSignatureRequest call
        static void cancelSignatureRequest(Client client, string signatureRequestId)
        {
            Console.WriteLine("└ Attempting to cancel signature request...");
            System.Threading.Thread.Sleep(3000);
            var retries = 15;
            while (retries > 0)
            {
                try
                {
                    // Cancel signature request
                    client.CancelSignatureRequest(signatureRequestId);
                    Console.WriteLine("└ Cancelled " + signatureRequestId);
                    break;
                }
                catch (ConflictException e)
                {
                    retries--;
                    Console.Write("└ Caught conflict exception: " + e.Message);
                    if (retries > 0)
                    {
                        Console.WriteLine(". Trying again in 2s...");
                        System.Threading.Thread.Sleep(2000);
                    }
                    else
                    {
                        Console.WriteLine(". Giving up!");
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            // Get API key from environment
            var apiKey = Environment.GetEnvironmentVariable("APIKEY");
            if (String.IsNullOrEmpty(apiKey))
            {
                throw new Exception("You must provide your HelloSign API key in the APIKEY environment variable.");
            }

            // Get API host
            string apiHost = Environment.GetEnvironmentVariable("APIHOST");
            if (String.IsNullOrEmpty(apiHost))
            {
                throw new Exception("You must specify the API host/domain via the APIHOST environment variable (e.g. 'api.hellosign.com').");
            }
            Console.WriteLine("Using HelloSign API at host: " + apiHost);

            // Client setup
            var client = new Client(apiKey);
            client.SetApiHost(apiHost);

            // Prepare some fake text files for upload
            byte[] file1 = System.Text.Encoding.ASCII.GetBytes("Test document, please sign at the end.");
            byte[] file2 = System.Text.Encoding.ASCII.GetBytes("Did I mention this is only a test?");
            byte[] textTagsFile1 = System.Text.Encoding.ASCII.GetBytes("This file has text tags:\n\n[sig|req|signer1]\n\n[initial|req|signer2]");
            byte[] pdfFile1 = Properties.Resources.Test_Document;

            // Get account
            var account = client.GetAccount();
            Console.WriteLine("My Account ID: " + account.AccountId);
            //var account = client.UpdateAccount(new Uri("http://example.com"));

            // Try (and fail) to create account that already exists
            try
            {
                var newAccount = client.CreateAccount("apidemos@hellosign.com");
                throw new Exception("Created account that should already exist: " + newAccount.EmailAddress);
            }
            catch (BadRequestException)
            {
                Console.WriteLine("Was successfully blocked from creating a pre-existing account.");
            }

            // Get team (or try to create one)
            Team team;
            try
            {
                team = client.GetTeam();
                Console.WriteLine("My Team Name: " + team.Name);
            }
            catch (NotFoundException)
            {
                try
                {
                    team = client.CreateTeam("Test Program");
                    Console.WriteLine("Created Team Named: " + team.Name);
                }
                catch (BadRequestException)
                {
                    Console.WriteLine("Couldn't get or create team.");
                }
            }

            // List signature requests
            var requests = client.ListSignatureRequests();
            Console.WriteLine("Found this many signature requests: " + requests.NumResults);
            foreach (var result in requests)
            {
                Console.WriteLine("Signature request: " + result.SignatureRequestId);
            }

            // List templates
            var templates = client.ListTemplates();
            Console.WriteLine("Found this many templates: " + templates.NumResults);
            foreach (var result in templates)
            {
                Console.WriteLine("Template: " + result.TemplateId);
            }

            // If any templates exist, get first one's download URL
            if (templates.NumResults > 0)
            {
                var firstTemplateId = templates.Items[0].TemplateId;

                // Get a download URL for this template
                var tDownloadUrl = client.GetTemplateFilesDownloadUrl(firstTemplateId);
                Console.WriteLine("Template download URL: " + tDownloadUrl.FileUrl + " (Expires at: " + tDownloadUrl.ExpiresAt + ")");

                // Download this template's files to disk as a PDF
                client.DownloadTemplateFiles(firstTemplateId, "template.pdf");
                Console.WriteLine("Downloaded Template PDF as template.pdf");

                // Get the template itself
                var template = client.GetTemplate(firstTemplateId);
                Console.WriteLine("Got Template: " + firstTemplateId);

                // Check the document for custom fields
                foreach(Document document in template.Documents) {
                    Console.WriteLine("└ Found Document: " + document.Name);
                    foreach(CustomField customField in document.CustomFields) {
                        Console.WriteLine(" └ Found Custom Field: " + customField.Name);
                    }
                }
            }

            // List Bulk Send Jobs
            var bulkSendJobInfos = client.ListBulkSendJobs();
            Console.WriteLine("Found this many bulk send jobs: " + bulkSendJobInfos.NumResults);
            foreach (BulkSendJobInfo job in bulkSendJobInfos)
            {
                string creator = job.IsCreator ? "by me" : "by another user";
                Console.WriteLine($"└ BulkSendJob: {job.BulkSendJobId} (Total: {job.Total}) Created {creator} on {job.CreatedAt}");
            }

            // Get a single Bulk Send Job
            if (bulkSendJobInfos.NumResults > 0)
            {
                BulkSendJob job = client.GetBulkSendJob(bulkSendJobInfos.Items[0]);
                BulkSendJobInfo jobInfo = job.JobInfo;
                string creator = jobInfo.IsCreator ? "by me" : "by another user";
                Console.WriteLine($"BulkSendJob: {jobInfo.BulkSendJobId} (Total: {jobInfo.Total}) Created {creator} on {jobInfo.CreatedAt}");

                // List its Signature Requests
                foreach (var result in job.Items)
                {
                    Console.WriteLine("└ Signature request: " + result.SignatureRequestId);
                }
            }

            // Send signature request
            var request = new SignatureRequest();
            request.Title = "NDA with Acme Co.";
            request.Subject = "The NDA we talked about";
            request.Message = "Please sign this NDA and then we can discuss more. Let me know if you have any questions.";
            request.AddSigner("jack@example.com", "Jack");
            request.AddSigner("jill@example.com", "Jill");
            request.AddCc("lawyer@example.com");
            request.AddFile(file1, "NDA.txt");
            request.AddFile(file2, "AppendixA.txt");
            request.Metadata.Add("custom_id", "1234");
            client.AdditionalParameters.Add("metadata[custom_text]", "NDA #9"); // Inject additional parameter by hand
            request.AllowDecline = true;
            request.SigningOptions = new SigningOptions
            {
                Draw = true,
                Type = true,
                Default = "type"
            };
            request.TestMode = true;
            var response = client.SendSignatureRequest(request);
            Console.WriteLine("New Signature Request ID: " + response.SignatureRequestId);

            // Remove additional parameter
            client.AdditionalParameters.Remove("metadata[custom_text]");

            // Get signature request (yes, it's redundant right here)
            var signatureRequest = client.GetSignatureRequest(response.SignatureRequestId);
            Console.WriteLine("Fetched request with Title: " + signatureRequest.Title);

            // Get a download URL for this signature request
            Console.WriteLine("Attempting to get a PDF link...");
            var retries = 15;
            while (retries > 0)
            {
                try
                {
                    var downloadUrl = client.GetSignatureRequestDownloadUrl(response.SignatureRequestId);
                    Console.WriteLine("Download URL: " + downloadUrl.FileUrl + " (Expires at: " + downloadUrl.ExpiresAt + ")");
                    break;
                }
                catch (Exception e) // Workaround for an API bug
                {
                    retries--;
                    Console.Write("└ Caught an exception: " + e.Message);
                    if (retries > 0)
                    {
                        Console.WriteLine(". Trying again in 2s...");
                        System.Threading.Thread.Sleep(2000);
                    }
                    else
                    {
                        Console.WriteLine(". Giving up!");
                    }
                }
            }

            // Download signature request
            Console.WriteLine("Attempting to download PDF...");
            retries = 15;
            while (retries > 0)
            {
                try
                {
                    client.DownloadSignatureRequestFiles(response.SignatureRequestId, "out.pdf");
                    Console.WriteLine("Downloaded PDF as out.pdf");
                    break;
                }
                catch (ConflictException e)
                {
                    retries--;
                    Console.Write("└ Caught conflict exception: " + e.Message);
                    if (retries > 0)
                    {
                        Console.WriteLine(". Trying again in 2s...");
                        System.Threading.Thread.Sleep(2000);
                    }
                    else
                    {
                        Console.WriteLine(". Giving up!");
                    }
                }
            }

            // Cancel signature request
            cancelSignatureRequest(client, response.SignatureRequestId);

            // Send signature request with text tags
            var ttRequest = new SignatureRequest();
            ttRequest.Title = "NDA with Acme Co.";
            ttRequest.Subject = "The NDA we talked about";
            ttRequest.Message = "Please sign this NDA and then we can discuss more. Let me know if you have any questions.";
            ttRequest.AddSigner("jack@example.com", "Jack");
            ttRequest.AddSigner("jill@example.com", "Jill");
            ttRequest.AddFile(textTagsFile1, "TextTagsDocument.txt");
            ttRequest.UseTextTags = true;
            ttRequest.HideTextTags = true;
            ttRequest.TestMode = true;
            var ttResponse = client.SendSignatureRequest(ttRequest);
            Console.WriteLine("New Text Tags Signature Request ID: " + ttResponse.SignatureRequestId);

            // Cancel text tags request
            cancelSignatureRequest(client, ttResponse.SignatureRequestId);

            // Send signature request with template
            if (TEMPLATE_ID.Length > 0) {
                var tRequest = new TemplateSignatureRequest();
                tRequest.AddTemplate(TEMPLATE_ID);
                tRequest.Subject = "Purchase Order";
                tRequest.Message = "Glad we could come to an agreement.";
                tRequest.AddSigner("Client", "george@example.com", "George");
                tRequest.AddCc("Accounting", "accounting@example.com");
                tRequest.AddCustomField("Cost", "$20,000", "Client", true);
                tRequest.TestMode = true;
                var tResponse = client.SendSignatureRequest(tRequest);
                Console.WriteLine("New Template Signature Request ID: " + tResponse.SignatureRequestId);
                Console.WriteLine("Custom field 'Cost' value is: " + tResponse.GetCustomField("Cost").Value);
                Console.WriteLine("Custom field 'Cost' editor is: " + tResponse.GetCustomField("Cost").Editor);

                // Cancel that signature request
                cancelSignatureRequest(client, tResponse.SignatureRequestId);
            }
            else {
                Console.WriteLine("Skipping TemplateSignatureRequest test.");
            }

            // Send signature request with path to file and form fields
            var fpRequest = new SignatureRequest();
            fpRequest.AddSigner("jack@example.com", "Jack");
            fpRequest.AddSigner("jill@example.com", "Jill");
            fpRequest.AddFile("out.pdf").WithFields(
                new FormField("chk1", FormField.TypeCheckbox,     1, 140, 72*1,  36, 36, true, 0),
                new FormField("txt1", FormField.TypeText,         1, 140, 72*2, 225, 20, true, 0, FormField.ValidationTypeEmailAddress),
                new FormField("dat1", FormField.TypeDateSigned,   1, 140, 72*3, 225, 52, true, 0),
                new FormField("sig1", FormField.TypeSignature,    1, 140, 72*4, 225, 52, true, 0),
                new FormField("sig2", FormField.TypeSignature,    1, 140, 72*5, 225, 52, true, 1)
            );
            fpRequest.Title = "File Path Test";
            fpRequest.TestMode = true;
            var fpResponse = client.SendSignatureRequest(fpRequest);
            Console.WriteLine("New Signature Request ID: " + fpResponse.SignatureRequestId);
            cancelSignatureRequest(client, fpResponse.SignatureRequestId);

            // Send signature request with form fields
            var ffRequest = new SignatureRequest();
            ffRequest.AddSigner("jack@example.com", "Jack");
            ffRequest.AddSigner("jill@example.com", "Jill");
            ffRequest.AddFile(pdfFile1, "TestDocument.pdf").WithFields(
                new FormField("chk1", FormField.TypeCheckbox,     1, 140, 72*1,  36, 36, true, 0),
                new FormField("txt1", FormField.TypeText,         1, 140, 72*2, 225, 20, true, 0, FormField.ValidationTypeEmailAddress),
                new FormField("dat1", FormField.TypeDateSigned,   1, 140, 72*3, 225, 52, true, 0),
                new FormField("sig1", FormField.TypeSignature,    1, 140, 72*4, 225, 52, true, 0),
                new FormField("sig2", FormField.TypeSignature,    1, 140, 72*5, 225, 52, true, 1)
            );
            ffRequest.Title = "Form Fields Test";
            ffRequest.TestMode = true;
            var ffResponse = client.SendSignatureRequest(ffRequest);
            Console.WriteLine("New Signature Request ID: " + ffResponse.SignatureRequestId);

            // Update form fields request
            var signatureId = ffResponse.Signatures[0].SignatureId;
            ffResponse = client.UpdateSignatureRequest(ffResponse.SignatureRequestId, signatureId, "jack-updated@example.com");
            Console.WriteLine("└ Updated request to email address: " + ffResponse.Signatures[0].SignerEmailAddress);

            // Cancel form fields request
            cancelSignatureRequest(client, ffResponse.SignatureRequestId);

            // List API apps
            var apiApps = client.ListApiApps();
            Console.WriteLine("Found this many API apps: " + apiApps.NumResults);
            foreach (var result in apiApps)
            {
                Console.WriteLine("API app: " + result.Name + " (" + result.ClientId + ")");
            }

            // Create API app
            var newApiApp = new ApiApp();
            DateTime.Now.ToShortTimeString();
            newApiApp.Name = "C# SDK Test App - " + DateTime.Now.ToString();
            newApiApp.Domain = "example.com";
            newApiApp.CallbackUrl = "https://example.com/callback";
            var aResponse = client.CreateApiApp(newApiApp);
            Console.WriteLine("New API App: " + aResponse.Name);

            // Get the new API app again (just for demonstration purposes)
            var apiApp = client.GetApiApp(aResponse.ClientId);
            var clientId = apiApp.ClientId;
                        
            //Update Primary Button Color on existing app
            var updatedApiApp = client.UpdateApiApp(clientId, new WhiteLabel () { PrimaryButtonColor = "#00b3e6"});
            Console.WriteLine($"Now my {updatedApiApp.Name} has Design Components. Only available with HelloSign");

            try
            {
                //Update Primary Button Color on existing app to an invalid Contrast
                client.UpdateApiApp(clientId, new WhiteLabel () { TextColor2 = "#323A3F"});
            }
            catch(BadRequestException ex)
            { 
                //[{"error_type":"invalid_contrast_ratio","element_name":"text_color2 and header_background_color"}]
                Console.WriteLine($"Not Allowed {ex.Message}");
            }

            // Create embedded signature request
            var eRequest = new SignatureRequest();
            eRequest.Title = "NDA with Acme Co.";
            eRequest.Subject = "The NDA we talked about";
            eRequest.Message = "Please sign this NDA and then we can discuss more. Let me know if you have any questions.";
            eRequest.AddSigner("jack@example.com", "Jack");
            eRequest.AddFile(file1, "NDA.txt");
            eRequest.Metadata.Add("custom_id", "1234");
            eRequest.Metadata.Add("custom_text", "NDA #9");
            eRequest.TestMode = true;
            var eResponse = client.CreateEmbeddedSignatureRequest(eRequest, clientId);
            Console.WriteLine("New Embedded Signature Request ID: " + eResponse.SignatureRequestId);

            // Get embedded signing URL
            var embedded = client.GetSignUrl(eResponse.Signatures[0].SignatureId);
            Console.WriteLine("└ First Signature Sign URL: " + embedded.SignUrl);

            // Cancel that embedded signature request
            cancelSignatureRequest(client, eResponse.SignatureRequestId);

            // Create unclaimed draft
            var draft = new SignatureRequest();
            draft.AddFile(file1, "Agreement.txt");
            draft.AddFile(file2, "Appendix.txt");
            draft.TestMode = true;
            var uResponse = client.CreateUnclaimedDraft(draft, UnclaimedDraft.Type.RequestSignature);
            Console.WriteLine("New Unclaimed Draft Claim URL: " + uResponse.ClaimUrl);

            // Create embedded unclaimed draft
            var eDraft = new SignatureRequest();
            eDraft.AddFile(file1, "Agreement.txt");
            eDraft.RequesterEmailAddress = "jack@hellosign.com";
            eDraft.TestMode = true;
            eDraft.HoldRequest = true;
            var euResponse = client.CreateUnclaimedDraft(eDraft, clientId);
            Console.WriteLine("New Embedded Unclaimed Draft Claim URL: " + euResponse.ClaimUrl);

            // Create embedded unclaimed draft with a template
            if (TEMPLATE_ID.Length > 0) {
                var teDraft = new TemplateSignatureRequest();
                teDraft.AddTemplate(TEMPLATE_ID);
                teDraft.RequesterEmailAddress = "jack@hellosign.com";
                teDraft.AddSigner("Client", "george@example.com", "George");
                teDraft.AddCc("Accounting", "accounting@example.com");
                teDraft.TestMode = true;
                var etuResponse = client.CreateUnclaimedDraft(teDraft, clientId);
                Console.WriteLine("New Embedded Unclaimed Draft with Template Claim URL: " + etuResponse.ClaimUrl);
            }

            // Create embedded template draft
            var etDraft = new EmbeddedTemplateDraft();
            etDraft.TestMode = true;
            etDraft.AddFile(file1, "NDA.txt");
            etDraft.Title = "Test Template";
            etDraft.Subject = "Please sign this document";
            etDraft.Message = "For your approval.";
            etDraft.AddSignerRole("Client", 0);
            etDraft.AddSignerRole("Witness", 1);
            etDraft.AddCcRole("Manager");
            etDraft.AddMergeField("Full Name", MergeField.FieldType.Text);
            etDraft.AddMergeField("Is Registered?", MergeField.FieldType.Checkbox);
            var etResponse = client.CreateEmbeddedTemplateDraft(etDraft, clientId);
            Console.WriteLine("New Embedded Template Draft with ID: " + etResponse.TemplateId);

            // Delete the API app we created
            client.DeleteApiApp(clientId);
            Console.WriteLine("Deleted test API App");

            // Get Report
            var reportRequest = new Report();
            reportRequest.StartDate = DateTime.Now.AddYears(-1);
            reportRequest.EndDate = DateTime.Now;
            reportRequest.ReportType = "user_activity, document_status";
            var reportResponse = client.CreateReport(reportRequest);
            Console.WriteLine($"Status for Report ({reportResponse.ReportType}) between {reportResponse.StartDate} - {reportResponse.EndDate}: {reportResponse.Success}");

            Console.WriteLine("Press ENTER to exit.");
            Console.Read(); // Keeps the output window open
        }
    }
}
