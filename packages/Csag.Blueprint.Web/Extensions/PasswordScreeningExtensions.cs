namespace Csag.Blueprint.Web.Extensions
{
    using Csag.Blueprint.Web.Validation.Passwords;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;

    /// <summary>
    /// Registers password screening with ASP.NET Core Identity.
    /// </summary>
    public static class PasswordScreeningExtensions
    {
        /// <summary>
        /// Adds <see cref="BlueprintPasswordValidator{TUser}"/> to Identity, so every password the
        /// <see cref="UserManager{TUser}"/> sets is screened against the common-password list, the configured
        /// context-specific words, the account's own names and the breached-password check. Also registers the
        /// <see cref="IBreachedPasswordChecker"/> it uses.
        /// </summary>
        /// <param name="builder">The Identity builder.</param>
        /// <returns>The Identity builder for chaining.</returns>
        public static IdentityBuilder AddBlueprintPasswordScreening(this IdentityBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.AddBlueprintBreachedPasswordChecker();

            // The validator is closed over the builder's user type, as IdentityBuilder.AddPasswordValidator does.
            builder.Services.AddScoped(
                typeof(IPasswordValidator<>).MakeGenericType(builder.UserType),
                typeof(BlueprintPasswordValidator<>).MakeGenericType(builder.UserType));

            return builder;
        }

        /// <summary>
        /// Registers <see cref="IBreachedPasswordChecker"/> and the <see cref="HttpClient"/> its online lookup uses.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddBlueprintBreachedPasswordChecker(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddHttpClient(BreachedPasswordChecker.HttpClientName, client =>
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Csag.Blueprint"));
            services.TryAddSingleton<IBreachedPasswordChecker, BreachedPasswordChecker>();

            return services;
        }
    }
}
