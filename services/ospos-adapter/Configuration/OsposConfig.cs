namespace OsposAdapter.Configuration;

public class OsposConfig
{
    public string Host { get; set; } = "ospos_mysql";
    public string Database { get; set; } = "ospos";
    public string User { get; set; } = "root";
    public string Password { get; set; } = "ospospass";
    public int Port { get; set; } = 3306;

    public string ConnectionString =>
        $"Server={Host};Port={Port};Database={Database};User={User};Password={Password};";
}
