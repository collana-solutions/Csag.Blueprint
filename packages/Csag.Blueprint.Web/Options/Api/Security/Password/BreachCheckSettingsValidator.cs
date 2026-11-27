namespace Csag.Blueprint.Web.Options.Api.Security.Password
{
    using FluentValidation;

    /// <summary>
    /// Validates <see cref="BreachCheckSettings"/> at startup, so a misconfigured check fails before the first
    /// password is set instead of silently accepting every password.
    /// </summary>
    public sealed class BreachCheckSettingsValidator : AbstractValidator<BreachCheckSettings>
    {
        public BreachCheckSettingsValidator()
        {
            this.RuleFor(x => x.Mode)
                .IsInEnum()
                .WithMessage("BreachCheck:Mode must be Disabled, Online or Offline");

            this.RuleFor(x => x.TimeoutMilliseconds)
                .InclusiveBetween(100, 30000)
                .WithMessage("BreachCheck:TimeoutMilliseconds must be between 100 and 30000");

            // The prefix is appended as a relative URI, so a base without a trailing slash would replace its last
            // segment instead of extending it.
            this.RuleFor(x => x.RangeApiBaseUrl)
                .Must(BeAbsoluteHttpsUrlEndingWithSlash)
                .When(x => x.Mode == BreachCheckMode.Online)
                .WithMessage("BreachCheck:RangeApiBaseUrl must be an absolute https URL that ends with '/'");

            this.RuleFor(x => x.OfflineRangeDirectory)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithMessage("BreachCheck:OfflineRangeDirectory is required when Mode is Offline")
                .Must(Directory.Exists)
                .WithMessage(x => $"BreachCheck:OfflineRangeDirectory '{x.OfflineRangeDirectory}' does not exist")
                .When(x => x.Mode == BreachCheckMode.Offline);
        }

        private static bool BeAbsoluteHttpsUrlEndingWithSlash(string? url) =>
            !string.IsNullOrWhiteSpace(url)
            && url.EndsWith('/')
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}
