using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Pos.Persistence.Diagnostics
{
    public sealed class SqlLoggerInterceptor : DbCommandInterceptor
    {
        private readonly ILogger<SqlLoggerInterceptor> _log;
        public SqlLoggerInterceptor(ILogger<SqlLoggerInterceptor> log) => _log = log;

        private void Dump(string where, DbCommand cmd)
        {
            var ps = string.Join(", ",
                cmd.Parameters.Cast<DbParameter>()
                   .Select(p => $"{p.ParameterName}={(p.Value ?? "NULL")}"));
            _log.LogInformation("[EF:{Where}] {Sql}\nPARAMS: {Params}", where, cmd.CommandText, ps);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData d, InterceptionResult<int> r)
        { Dump("NonQuery", c); return base.NonQueryExecuting(c, d, r); }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> r)
        { Dump("Reader", c); return base.ReaderExecuting(c, d, r); }

        public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData d, InterceptionResult<object> r)
        { Dump("Scalar", c); return base.ScalarExecuting(c, d, r); }
    }
}
