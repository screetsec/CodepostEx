namespace CodepostEx.Output;

public static class OutputPaths
{
    public static string HistoryZip(string root, string tool) =>
        Path.Combine(root, "Artifacts", $"{tool}_History_{Stamp()}.zip");

    public static string ChatsJson(string root, string tool) =>
        Path.Combine(root, "AIChats", $"{tool}_AI_Chats.json");

    public static string ChatsTxt(string root, string tool) =>
        Path.Combine(root, "AIChats", $"{tool}_AI_Chats.txt");

    public static string ChatsHtml(string root, string tool) =>
        Path.Combine(root, "Reports", $"{tool}_Report.html");

    public static string WorkspaceTasksJson(string workspacePath) =>
        Path.Combine(workspacePath, ".vscode", "tasks.json");

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");
}
