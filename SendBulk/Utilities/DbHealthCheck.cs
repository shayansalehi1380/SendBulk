using System.Data.SqlClient;

public class DbHealthCheck
{
    private readonly string _connectionString;

    public DbHealthCheck(string connectionString)
    {
        _connectionString = connectionString;
    }

    public bool CheckConnection()
    {
        try
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open(); // اگر اینجا exception نده یعنی اتصال برقرار شده
                Console.WriteLine("Connect To Database Is Success.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Faild Connect To Database. " + ex.Message);
            return false;
        }
    }
}