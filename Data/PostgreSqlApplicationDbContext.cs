using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

public sealed class PostgreSqlApplicationDbContext(DbContextOptions<PostgreSqlApplicationDbContext> options)
    : ApplicationDbContext(options)
{
}
