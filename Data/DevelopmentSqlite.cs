using System.Runtime.InteropServices;
namespace NileShop.Data;
public static class DevelopmentSqlite
{
    public static void Initialize()
    {
        if (OperatingSystem.IsLinux())
            NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_sqlite3).Assembly,
                (name, assembly, path) => name == "sqlite3" ? NativeLibrary.Load("libsqlite3.so.0") : IntPtr.Zero);
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
    }
}
