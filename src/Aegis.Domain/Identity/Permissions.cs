namespace Aegis.Domain.Identity;

/// <summary>
/// The catalogue of permissions the system recognises.
/// </summary>
/// <remarks>
/// <para>
/// Authorization decisions are made against these, never against role names. Roles are a packaging
/// convenience for administrators; permissions are the unit of enforcement. The distinction earns
/// itself the first time a customer asks for "a dispatcher who can also approve budgets" — with
/// role checks scattered through handlers that is a code change and a release, with permission
/// checks it is an afternoon in the admin UI.
/// </para>
/// <para>
/// Names follow <c>module.resource.action</c>, so a prefix match answers "everything this user can
/// do to assets" without an enumeration.
/// </para>
/// <para>
/// Declared as constants rather than an enum on purpose. Permissions are persisted in role rows and
/// issued as JWT claims, and an enum's integer value is positional — inserting a member in the
/// middle silently reassigns the meaning of every stored row and every unexpired token.
/// </para>
/// </remarks>
public static class Permissions
{
    /// <summary>Organization and tenant administration.</summary>
    public static class Organizations
    {
        public const string View = "organizations.view";
        public const string Update = "organizations.update";
        public const string ManageMembers = "organizations.members.manage";
        public const string ManageDistricts = "organizations.districts.manage";
    }

    /// <summary>User and role administration.</summary>
    public static class Users
    {
        public const string View = "users.view";
        public const string Create = "users.create";
        public const string Update = "users.update";
        public const string Deactivate = "users.deactivate";
        public const string ResetPassword = "users.password.reset";
        public const string ManageRoles = "users.roles.manage";
    }

    /// <summary>Asset registry.</summary>
    public static class Assets
    {
        public const string View = "assets.view";
        public const string Create = "assets.create";
        public const string Update = "assets.update";
        public const string Decommission = "assets.decommission";
        public const string Export = "assets.export";
    }

    /// <summary>Incident intake and triage.</summary>
    public static class Incidents
    {
        public const string View = "incidents.view";
        public const string Report = "incidents.report";
        public const string Triage = "incidents.triage";
        public const string Close = "incidents.close";
    }

    /// <summary>Work order dispatch and completion.</summary>
    public static class WorkOrders
    {
        public const string View = "workorders.view";
        public const string Create = "workorders.create";
        public const string Assign = "workorders.assign";
        public const string Complete = "workorders.complete";
        public const string Approve = "workorders.approve";
    }

    /// <summary>Preventive and predictive maintenance.</summary>
    public static class Maintenance
    {
        public const string View = "maintenance.view";
        public const string Schedule = "maintenance.schedule";
        public const string Configure = "maintenance.configure";
    }

    /// <summary>Analytics and reporting.</summary>
    public static class Analytics
    {
        public const string ViewOperational = "analytics.operational.view";
        public const string ViewExecutive = "analytics.executive.view";
        public const string Export = "analytics.export";
    }

    /// <summary>Audit and activity history.</summary>
    public static class Audit
    {
        public const string View = "audit.view";
        public const string Export = "audit.export";
    }

    /// <summary>AI-assisted features.</summary>
    public static class Ai
    {
        public const string UseAssistant = "ai.assistant.use";
        public const string AnalyseImages = "ai.vision.use";
    }

    /// <summary>
    /// Every permission the system defines, for validating role assignments.
    /// </summary>
    /// <remarks>
    /// Granting a permission that no code checks produces a role that appears to confer access and
    /// does not, which is a support ticket nobody can reproduce. Validating against this set turns
    /// a typo into an immediate, specific error.
    /// </remarks>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Organizations.View, Organizations.Update, Organizations.ManageMembers, Organizations.ManageDistricts,
        Users.View, Users.Create, Users.Update, Users.Deactivate, Users.ResetPassword, Users.ManageRoles,
        Assets.View, Assets.Create, Assets.Update, Assets.Decommission, Assets.Export,
        Incidents.View, Incidents.Report, Incidents.Triage, Incidents.Close,
        WorkOrders.View, WorkOrders.Create, WorkOrders.Assign, WorkOrders.Complete, WorkOrders.Approve,
        Maintenance.View, Maintenance.Schedule, Maintenance.Configure,
        Analytics.ViewOperational, Analytics.ViewExecutive, Analytics.Export,
        Audit.View, Audit.Export,
        Ai.UseAssistant, Ai.AnalyseImages,
    };

    /// <summary>Returns true when the supplied name is a recognised permission.</summary>
    public static bool IsDefined(string? permission) =>
        !string.IsNullOrWhiteSpace(permission) && All.Contains(permission);
}

/// <summary>
/// Role names seeded for every new organization.
/// </summary>
/// <remarks>
/// Starting templates, not fixed definitions. Each organization gets its own editable copy, because
/// a water utility's idea of a "Supervisor" is not a road authority's, and a shared immutable role
/// forces one of them into a shape that does not fit.
/// </remarks>
public static class SystemRoles
{
    /// <summary>Full control within the organization.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Plans work and approves completion.</summary>
    public const string Supervisor = "Supervisor";

    /// <summary>Triages incidents and assigns crews.</summary>
    public const string Dispatcher = "Dispatcher";

    /// <summary>Executes work orders in the field, typically from the offline mobile client.</summary>
    public const string Technician = "Technician";

    /// <summary>Reads dashboards and reports without operational write access.</summary>
    public const string Analyst = "Analyst";

    /// <summary>Default permission sets for the seeded roles.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> DefaultPermissions { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Administrator] = Permissions.All,

            [Supervisor] = new HashSet<string>(StringComparer.Ordinal)
            {
                Permissions.Organizations.View,
                Permissions.Users.View,
                Permissions.Assets.View, Permissions.Assets.Create, Permissions.Assets.Update,
                Permissions.Assets.Export,
                Permissions.Incidents.View, Permissions.Incidents.Triage, Permissions.Incidents.Close,
                Permissions.WorkOrders.View, Permissions.WorkOrders.Create, Permissions.WorkOrders.Assign,
                Permissions.WorkOrders.Approve,
                Permissions.Maintenance.View, Permissions.Maintenance.Schedule,
                Permissions.Maintenance.Configure,
                Permissions.Analytics.ViewOperational, Permissions.Analytics.Export,
                Permissions.Audit.View,
                Permissions.Ai.UseAssistant,
            },

            [Dispatcher] = new HashSet<string>(StringComparer.Ordinal)
            {
                Permissions.Assets.View,
                Permissions.Incidents.View, Permissions.Incidents.Report, Permissions.Incidents.Triage,
                Permissions.WorkOrders.View, Permissions.WorkOrders.Create, Permissions.WorkOrders.Assign,
                Permissions.Maintenance.View,
                Permissions.Analytics.ViewOperational,
                Permissions.Ai.UseAssistant,
            },

            // Deliberately narrow. A field technician's device is the one most likely to be lost,
            // stolen or used on an untrusted network, so it should carry the least authority that
            // still lets the job get done.
            [Technician] = new HashSet<string>(StringComparer.Ordinal)
            {
                Permissions.Assets.View,
                Permissions.Incidents.View, Permissions.Incidents.Report,
                Permissions.WorkOrders.View, Permissions.WorkOrders.Complete,
                Permissions.Maintenance.View,
                Permissions.Ai.AnalyseImages,
            },

            [Analyst] = new HashSet<string>(StringComparer.Ordinal)
            {
                Permissions.Assets.View, Permissions.Assets.Export,
                Permissions.Incidents.View,
                Permissions.WorkOrders.View,
                Permissions.Maintenance.View,
                Permissions.Analytics.ViewOperational, Permissions.Analytics.ViewExecutive,
                Permissions.Analytics.Export,
                Permissions.Ai.UseAssistant,
            },
        };
}
