namespace Aras.SreHub.Services;

public sealed record SecureVaultProfile(string Directory, string Purpose, string[] Types, string[] SeedFolders)
{
    public static readonly SecureVaultProfile Operations = new(
        "secure-files",
        "SreHub.SecureFiles.v1",
        ["Kubeconfig", "Credentials", "Certificate", "Config", "Other"],
        []);

    public static readonly SecureVaultProfile Personal = new(
        "personal-files",
        "SreHub.PersonalFiles.v1",
        ["Identity", "Finance", "Insurance", "Medical", "Fitness", "Education", "Credentials", "Other"],
        ["Identity and Documents", "Finance and Tax", "Insurance", "Medical", "Fitness and Health", "Education and Certificates", "Credentials and Keys", "Career and Resume"]);
}
