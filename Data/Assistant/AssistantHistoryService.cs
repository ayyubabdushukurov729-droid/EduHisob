using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data.Assistant;

public sealed record ConversationSummary(int Id, string Title, DateTime UpdatedAtUtc, int MessageCount);

public sealed record SavedMessage(string Text, bool FromUser, bool IsError, IReadOnlyList<AssistantStep> Steps);

// Yordamchi suhbatlarini saqlaydi. Har bir amal joriy foydalanuvchiga bog'langan:
// boshqa xodimning suhbatini o'qib ham, o'chirib ham bo'lmaydi.
public sealed class AssistantHistoryService(IDbContextFactory<ApplicationDbContext> contextFactory, CenterAccess access)
{
    private const int TitleLength = 60;

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync()
    {
        var user = await access.GetAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.AssistantConversations.AsNoTracking()
            .Where(c => c.OrganizationId == user.OrganizationId && c.UserId == user.UserId)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Take(100)
            .Select(c => new ConversationSummary(c.Id, c.Title, c.UpdatedAtUtc, db.AssistantMessages.Count(m => m.ConversationId == c.Id)))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<SavedMessage>> LoadAsync(int conversationId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        await RequireOwnAsync(db, conversationId);
        return (await db.AssistantMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Id)
                .ToListAsync())
            .Select(m => new SavedMessage(m.Text, m.FromUser, m.IsError, ParseSteps(m.Steps)))
            .ToList();
    }

    // Savol va javobni saqlaydi. Suhbat hali yo'q bo'lsa, birinchi savoldan nom olib yaratadi. Suhbat ID sini qaytaradi.
    public async Task<int> SaveExchangeAsync(int? conversationId, string question, string answer, bool isError, IReadOnlyList<AssistantStep> steps)
    {
        var user = await access.GetAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        AssistantConversation conversation;
        if (conversationId is int id)
        {
            conversation = await RequireOwnAsync(db, id);
        }
        else
        {
            var title = question.ReplaceLineEndings(" ").Trim();
            conversation = new AssistantConversation
            {
                OrganizationId = user.OrganizationId,
                UserId = user.UserId,
                Title = title.Length > TitleLength ? title[..TitleLength].TrimEnd() + "…" : title
            };
            db.AssistantConversations.Add(conversation);
            await db.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        conversation.UpdatedAtUtc = now;
        db.AssistantMessages.Add(new AssistantMessage { ConversationId = conversation.Id, FromUser = true, Text = Limit(question), CreatedAtUtc = now });
        db.AssistantMessages.Add(new AssistantMessage
        {
            ConversationId = conversation.Id,
            FromUser = false,
            IsError = isError,
            Text = Limit(answer),
            Steps = string.Join(',', steps.Select(s => (s.IsError ? "!" : "") + s.ToolName)),
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    public async Task DeleteAsync(int conversationId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var conversation = await RequireOwnAsync(db, conversationId);
        db.AssistantConversations.Remove(conversation);
        await db.SaveChangesAsync();
    }

    public async Task<int> DeleteAllAsync()
    {
        var user = await access.GetAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        var mine = db.AssistantConversations.Where(c => c.OrganizationId == user.OrganizationId && c.UserId == user.UserId);
        await db.AssistantMessages.Where(m => mine.Any(c => c.Id == m.ConversationId)).ExecuteDeleteAsync();
        return await mine.ExecuteDeleteAsync();
    }

    private async Task<AssistantConversation> RequireOwnAsync(ApplicationDbContext db, int conversationId)
    {
        var user = await access.GetAsync();
        return await db.AssistantConversations.SingleOrDefaultAsync(c =>
                   c.Id == conversationId && c.OrganizationId == user.OrganizationId && c.UserId == user.UserId)
               ?? throw new InvalidOperationException("Suhbat topilmadi.");
    }

    private static string Limit(string text) => text.Length > 20000 ? text[..20000] : text;

    private static IReadOnlyList<AssistantStep> ParseSteps(string steps) => steps
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(step => new AssistantStep(step.TrimStart('!'), step.StartsWith('!')))
        .ToList();
}
