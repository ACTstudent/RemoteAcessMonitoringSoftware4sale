namespace Server.Services;

/// <summary>
/// One step of getting a fresh CAMS server ready for a classroom.
/// </summary>
/// <param name="Title">What the step is.</param>
/// <param name="Done">Whether it has been done.</param>
/// <param name="Why">What does not work until it is done. Stated in terms of
/// what a teacher or student would experience, not in terms of the database.</param>
/// <param name="NextAction">The words on the link that does it.</param>
/// <param name="Href">Where that link goes, or null when the step is not
/// something the portal can do - installing the agent on a workstation, for
/// instance.</param>
public sealed record SetupStep(
    string Title,
    bool Done,
    string Why,
    string NextAction,
    string? Href);

/// <summary>
/// What still stands between a fresh install and a working classroom.
///
/// FLOW-01. The dashboard showed counts - students, teachers, workstations - and
/// left the administrator to work out for themselves which zero mattered and in
/// what order to fix it. A count is not an instruction. Every step here says
/// what does not work until it is done, and where to go to do it.
///
/// Deliberately derived rather than stored: a checklist with its own state goes
/// stale the moment someone deletes the last class, and then confidently reports
/// a setup that is no longer complete.
/// </summary>
public static class SetupChecklist
{
    /// <param name="teacherCount">Active teachers.</param>
    /// <param name="classCount">Classes that are not archived.</param>
    /// <param name="workstationCount">Registered workstations.</param>
    /// <param name="installerReady">Whether the client installer is staged for download.</param>
    /// <param name="rootCertificateAvailable">Whether the public root certificate can be handed out.</param>
    /// <param name="everConnected">Whether any student agent has ever reached this server.</param>
    public static IReadOnlyList<SetupStep> Build(
        int teacherCount,
        int classCount,
        int workstationCount,
        bool installerReady,
        bool rootCertificateAvailable,
        bool everConnected)
    {
        return new[]
        {
            new SetupStep(
                "Add a teacher",
                teacherCount > 0,
                "Without a teacher account nobody can run a laboratory session; the " +
                "portal is only useful to administrators until one exists.",
                "Add a teacher",
                "/Admin/Teachers"),

            new SetupStep(
                "Create a class",
                classCount > 0,
                "Students are monitored through the class they belong to. Until a " +
                "class exists there is nothing to enrol them into.",
                "Create a class",
                "/Admin/Classes"),

            new SetupStep(
                "Register a workstation",
                workstationCount > 0,
                "A student signs in at a workstation. One that the server does not " +
                "know about will be refused.",
                "Register a workstation",
                "/Admin/Computers"),

            new SetupStep(
                "Stage the client installer",
                installerReady,
                "The Deployment Hub is where the student client is downloaded from. " +
                "Until the installer is staged there is nothing to install on a " +
                "workstation.",
                "Open the Deployment Hub",
                "/Admin/Deployment"),

            new SetupStep(
                "Publish the root certificate",
                rootCertificateAvailable,
                "A workstation that does not trust this server's certificate cannot " +
                "connect, and the failure looks like a network fault rather than a " +
                "trust one. Only the public certificate is distributed.",
                "Download the root certificate",
                "/Admin/Deployment"),

            new SetupStep(
                "Connect the first workstation",
                everConnected,
                "Nothing above proves a student can actually reach the server. This " +
                "is the step that does.",
                "Install the client on a workstation and sign in there",
                null)
        };
    }

    /// <summary>Whether every step is done, and the setup panel can be put away.</summary>
    public static bool IsComplete(IReadOnlyList<SetupStep> steps) => steps.All(step => step.Done);

    /// <summary>
    /// The step to do next: the first one outstanding. An administrator faced
    /// with six red items needs to be told which one to start with.
    /// </summary>
    public static SetupStep? NextStep(IReadOnlyList<SetupStep> steps) =>
        steps.FirstOrDefault(step => !step.Done);
}
