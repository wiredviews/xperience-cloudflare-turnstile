using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Web;
using CMS.Base.Internal;
using CMS.Core;
using CMS.DataEngine;
using CMS.Helpers;
using CMS.SiteProvider;
using Kentico.Forms.Web.Mvc;
using Newtonsoft.Json;
using XperienceCommunity.Cloudflare.Turnstile.Components.FormComponents;

[assembly: RegisterFormComponent(
    TurnstileFormComponent.IDENTIFIER,
    typeof(TurnstileFormComponent),
    "{$xperiencecommunity.formbuilder.component.turnstile.name$}",
    Description = "{$xperiencecommunity.formbuilder.component.turnstile.description$}", IconClass = "icon-recaptcha", ViewName = "~/Components/FormComponents/TurnstileFormComponent.cshtml")]

namespace XperienceCommunity.Cloudflare.Turnstile.Components.FormComponents;

/// <summary>
/// Cloudflare Turnstile form component.
/// </summary>
/// <remarks>
/// Large portions of functionality sourced from Kentico Xperience 13.0 source RecaptchaComponent
/// </remarks>
public class TurnstileFormComponent : FormComponent<TurnstileFormComponentProperties, string>
{
    /// <summary>
    /// <see cref="TurnstileFormComponent"/> component identifier.
    /// </summary>
    public const string IDENTIFIER = "xperiencecommunity.formcomponent.turnstile";

    private static readonly Lazy<HashSet<string>> mFullLanguageCodes = new(() =>
    {
        string[] codes = new[] { "zh-HK", "zh-CN", "zh-TW", "en-GB", "fr-CA", "de-AT", "de-CH", "pt-BR", "pt-PT" };

        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    });

    private string mLanguage = "";
    private bool? mSkipTurnstile;


    /// <summary>
    /// Holds nothing and is here just because it is required.
    /// </summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// Turnstile site key from https://dash.cloudflare.com/.
    /// </summary>
    public string PublicKey => SettingsKeyInfoProvider.GetValue("CMSTurnstilePublicKey", SiteContext.CurrentSiteID);


    /// <summary>
    /// Turnstile private key from https://dash.cloudflare.com/.
    /// </summary>
    public string SecretKey => SettingsKeyInfoProvider.GetValue("CMSTurnstileSecretKey", SiteContext.CurrentSiteID);

    /// <summary>
    /// Optional. Forces the Turnstile to render in a specific language.
    /// Auto-detects the user's language if unspecified.
    /// Currently not yet supported https://community.cloudflare.com/t/change-language-for-turnstile/426108.
    /// </summary>
    public string Language
    {
        get
        {
            if (string.IsNullOrEmpty(mLanguage))
            {
                var currentCulture = CultureInfo.CurrentCulture;

                mLanguage = mFullLanguageCodes.Value.Contains(currentCulture.Name) ? currentCulture.Name : currentCulture.TwoLetterISOLanguageName;
            }

            return mLanguage;
        }
        set => mLanguage = value;
    }

    /// <summary>
    /// Determines whether the component is configured and allowed to be displayed.
    /// </summary>
    public bool IsConfigured => AreKeysConfigured && !SkipTurnstile;

    /// <summary>
    /// Indicates whether to skip the Turnstile validation.
    /// Useful for testing platform. Can be set using TurnstileSkipValidation in AppSettings.
    /// </summary>
    /// <remarks>
    /// Testing can also be performed using Turnstile's test API keys https://developers.cloudflare.com/turnstile/frequently-asked-questions/#are-there-sitekeys-and-secret-keys-that-can-be-used-for-testing
    /// </remarks>
    private bool SkipTurnstile
    {
        get
        {
            if (!mSkipTurnstile.HasValue)
            {
                mSkipTurnstile = ValidationHelper.GetBoolean(Service.Resolve<IAppSettingsService>()["TurnstileSkipValidation"], false);
            }

            return mSkipTurnstile.Value;
        }
    }

    /// <summary>
    /// Indicates whether both required keys are configured in the Settings application.
    /// </summary>
    private bool AreKeysConfigured => !string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(SecretKey);

    /// <summary>
    /// Label "for" cannot be used for this component. 
    /// </summary>
    public override string LabelForPropertyName => "";

    /// <summary>
    /// Returns empty string since the <see cref="Value"/> does not hold anything.
    /// </summary>
    /// <returns>Returns the value of the form component.</returns>
    public override string GetValue() => string.Empty;

    /// <summary>
    /// Does nothing since the <see cref="Value"/> does not need to hold anything.
    /// </summary>
    /// <param name="value">Value to be set.</param>
    public override void SetValue(string value)
    {
        // the Value does not need to hold anything
    }

    /// <summary>
    /// Performs validation of the Turnstile component.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>A collection that holds failed-validation information.</returns>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = new List<ValidationResult>();
        errors.AddRange(base.Validate(validationContext));

        bool isRenderedInAdminUI = VirtualContext.IsInitialized;

        if (!IsConfigured || isRenderedInAdminUI)
        {
            return errors;
        }

        var httpContext = Service.Resolve<IHttpContextRetriever>().GetContext();
        string response = httpContext.Request.Form.TryGetValue("cf-turnstile-response", out var value)
            ? value.ToString()
            : string.Empty;

        var validator = new TurnstileValidator(PublicKey, SecretKey, RequestContext.UserHostAddress, response);

        var validationResult = validator.Validate();

        if (validationResult is not null)
        {
            if (!string.IsNullOrEmpty(validationResult.ErrorMessage))
            {
                errors.Add(new ValidationResult(validationResult.ErrorMessage));
            }
        }
        else
        {
            errors.Add(new ValidationResult(ResHelper.GetString("recaptcha.error.serverunavailable")));
        }

        return errors;
    }
}

public class TurnstileFormComponentProperties : FormComponentProperties<string>
{
    //
    // Summary:
    //     Gets or sets the default value of the form component and underlying field.
    public override string DefaultValue { get; set; } = "";

    //
    // Summary:
    //     Gets or sets value indicating whether the underlying field is required. False
    //     by default. If false, the form component's implementation must accept nullable
    //     input.
    public override bool Required { get; set; }

    /// <summary>
    /// Represents the color theme of the component (light or dark).
    /// </summary>
    /// <remarks>
    /// https://developers.cloudflare.com/turnstile/get-started/client-side-rendering/#configurations
    /// </remarks>
    /// <value></value>
    [EditingComponent("Kentico.DropDown", Label = "{$xperiencecommunity.formbuilder.component.turnstile.properties.theme$}", Order = 1)]
    [EditingComponentProperty("DataSource", "light;{$xperiencecommunity.formbuilder.component.turnstile.properties.theme.light$}\r\ndark;{$xperiencecommunity.formbuilder.component.turnstile.properties.theme.dark$}\r\nauto;{$xperiencecommunity.formbuilder.component.turnstile.properties.theme.auto$}")]
    public string Theme { get; set; } = "auto";

    /// <summary>
    /// The widget size. Can take the following values: normal, compact.
    /// </summary>
    /// <remarks>
    /// https://developers.cloudflare.com/turnstile/get-started/client-side-rendering/#configurations
    /// </remarks>
    /// <value></value>
    [EditingComponent("Kentico.DropDown", Label = "{$xperiencecommunity.formbuilder.component.turnstile.properties.widgetsize$}", Order = 2)]
    [EditingComponentProperty("DataSource", "normal;{$xperiencecommunity.formbuilder.component.turnstile.properties.widgetsize.normal$}\r\ncompact;{$xperiencecommunity.formbuilder.component.turnstile.properties.widgetsize.compact$}")]
    public string WidgetSize { get; set; } = "normal";

    /// <summary>
    /// A customer value that can be used to differentiate widgets under the same sitekey in analytics and which is returned upon validation. This can only contain up to 32 alphanumeric characters including _ and -.
    /// </summary>
    /// <remarks>
    /// https://developers.cloudflare.com/turnstile/get-started/client-side-rendering/#configurations
    /// </remarks>
    /// <value></value>
    [EditingComponent(TextInputComponent.IDENTIFIER, Label = "{$xperiencecommunity.formbuilder.component.turnstile.properties.action$}", Order = 3)]
    [MaxLength(32)]
    [RegexStringValidator("^([a-zA-Z-_])+$")]
    public string Action { get; set; } = "xperience-form-builder";

    //
    // Summary:
    //     Initializes a new instance of the TurnstileProperties class.
    //
    // Remarks:
    //     The constructor initializes the base class to data type CMS.DataEngine.FieldDataType.Text
    //     and size 1.
    public TurnstileFormComponentProperties()
        : base("text", 1, -1)
    {
    }
}

/// <summary>
/// Calls the Cloudflare Turnstile server to validate the answer to a Turnstile challenge.
/// </summary>
public class TurnstileValidator
{
    private const string VERIFYURL = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>
    /// The shared key between the site and Turnstile.
    /// </summary>
    private readonly string secretKey;
    private readonly string publicKey;
    /// <summary>
    /// The user's IP address.
    /// </summary>
    private readonly string remoteIP;
    /// <summary>
    /// The user response token provided by Turnstile, verifying the user on your site.
    /// </summary>
    private readonly string response;

    public TurnstileValidator(string publicKey, string secretKey, string remoteIP, string formResponseValue)
    {
        this.publicKey = publicKey;
        this.secretKey = secretKey;

        var ip = IPAddress.Parse(remoteIP);

        if (ip == null ||
            (ip.AddressFamily != AddressFamily.InterNetwork &&
            ip.AddressFamily != AddressFamily.InterNetworkV6))
        {
            throw new ArgumentException("Expecting an IP address, got " + ip);
        }

        this.remoteIP = ip.ToString();
        response = formResponseValue;
    }

    /// <summary>
    /// Validate Turnstile response
    /// </summary>
    public TurnstileResponse? Validate()
    {
        var log = Service.Resolve<IEventLogService>();

        // Prepare web request
        var content = new FormUrlEncodedContent(new Dictionary<string, string>()
        {
            ["secret"] = HttpUtility.UrlEncode(secretKey),
            ["remoteip"] = HttpUtility.UrlEncode(remoteIP),
            ["response"] = HttpUtility.UrlEncode(response),
            ["sitekey"] = HttpUtility.UrlEncode(publicKey)
        });

        var request = new HttpRequestMessage()
        {
            RequestUri = new Uri(VERIFYURL),
            Method = HttpMethod.Post,
            Version = new Version(1, 0),
            Content = content
        };
        request.Headers.Add("User-Agent", "Turnstile/ASP.NET");

        // Get validation response
        try
        {
            using var client = new HttpClient();
            var response = client.Send(request);
            string jsonResult = response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            return JsonConvert.DeserializeObject<TurnstileResponse>(jsonResult);
        }
        catch (WebException ex)
        {
            log.LogException(
                nameof(TurnstileFormComponent),
                "VALIDATE",
                ex);

            return null;
        }
    }
}

/// <summary>
/// Encapsulates a response from Turnstile web service.
/// </summary>
public class TurnstileResponse
{
    /// <summary>
    /// Indicates whether the Turnstile validation was successful.
    /// </summary>
    [JsonProperty("success")]
    public bool IsValid { get; set; }


    /// <summary>
    /// The hostname of the site where the Turnstile was solved
    /// </summary>
    [JsonProperty("hostname")]
    public string HostName { get; set; } = "";


    /// <summary>
    /// Timestamp of the challenge load.
    /// </summary>
    [JsonProperty("challenge_ts")]
    public DateTime TimeStamp { get; set; }


    /// <summary>
    /// Error codes explaining why Turnstile validation failed.
    /// </summary>
    [JsonProperty("error-codes")]
    public IEnumerable<string> ErrorCodes { get; set; } = Enumerable.Empty<string>();


    /// <summary>
    /// Aggregated error message from all the error codes.
    /// </summary>
    [JsonIgnore]
    public string ErrorMessage => string.Join(" ", ErrorCodes.Select(x => ResHelper.GetString("recaptcha.error." + x)));
}
