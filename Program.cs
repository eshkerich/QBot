using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class Program
{
    private static DiscordSocketClient _client;
    private static Dictionary<ulong, bool> _awaitingResponses = new Dictionary<ulong, bool>();

    private const string BOT_TOKEN = "YOUR_TOKEN";
    private const ulong PUBLIC_CHANNEL_ID = 123456789012345678;
    private const ulong MOD_CHANNEL_ID = 123456789123456789;

    public static async Task Main(string[] args)
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged |
                            GatewayIntents.MessageContent |
                            GatewayIntents.DirectMessages
        });

        _client.Ready += ClientReady;
        _client.ButtonExecuted += ButtonExecuted;
        _client.MessageReceived += MessageReceived;

        await _client.LoginAsync(TokenType.Bot, BOT_TOKEN);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private static async Task ClientReady()
    {
        Console.WriteLine($"{_client.CurrentUser.Username} ready!");
        var publicChannel = _client.GetChannel(PUBLIC_CHANNEL_ID) as IMessageChannel;
        if (publicChannel != null) await SendBotMessage(publicChannel);
    }

    private static async Task SendBotMessage(IMessageChannel channel)
    {
        var messages = await channel.GetMessagesAsync().FlattenAsync();
        foreach (var msg in messages)
        {
            if (msg.Author.Id == _client.CurrentUser.Id)
                await msg.DeleteAsync();
        }

        var button = new ButtonBuilder
        {
            CustomId = "create_ticket",
            Label = "Написать",
            Style = ButtonStyle.Primary,
            Emote = new Emoji("✉️")
        };

        var component = new ComponentBuilder().WithButton(button);

        var embed = new EmbedBuilder()
            .WithTitle("Обратная связь")
            .WithDescription("Нажмите кнопку ниже, чтобы отправить вопрос или предложение модераторам.")
            .WithColor(Color.Blue)
            .Build();

        await channel.SendMessageAsync(embed: embed, components: component.Build());
    }

    private static async Task ButtonExecuted(SocketMessageComponent component)
    {
        try
        {
            await component.DeferAsync(ephemeral: true);

            var dmChannel = await component.User.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(
                "Пожалуйста, напишите ваш вопрос или предложение. " +
                "Отправьте сообщение в этот чат, и оно будет переслано модераторам.");

            _awaitingResponses[component.User.Id] = true;

            await component.FollowupAsync(
                "Проверьте ваши личные сообщения с ботом",
                ephemeral: true);
        }
        catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode.HasValue && (int)httpEx.DiscordCode.Value == 50007)
        {
            await component.FollowupAsync(
                "Не удалось отправить вам сообщение. " +
                "Проверьте, что у вас открыты ЛС для этого сервера.",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
            await component.FollowupAsync(
                "Произошла ошибка. Попробуйте позже.",
                ephemeral: true);
        }
    }

    private static async Task MessageReceived(SocketMessage message)
    {
        if (message.Author.IsBot ||  !(message is SocketUserMessage) || message.Channel is not IDMChannel)
            return;

        if (_awaitingResponses.TryGetValue(message.Author.Id, out bool isWaiting) && isWaiting)
        {
            try
            {
                var modChannel = _client.GetChannel(MOD_CHANNEL_ID) as IMessageChannel;
                if (modChannel == null) return; 
                var embed = new EmbedBuilder()
                    .WithTitle("Новое обращение")
                    .WithDescription(message.Content)
                    .WithColor(Color.Blue)
                    .WithAuthor(message.Author)
                    .AddField("Пользователь", $"{message.Author.Mention} (ID: {message.Author.Id})")
                    .WithCurrentTimestamp()
                    .Build();

                await modChannel.SendMessageAsync(embed: embed);
                await message.Channel.SendMessageAsync("Ваше сообщение было отправлено модераторам!");

                _awaitingResponses.Remove(message.Author.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                await message.Channel.SendMessageAsync("Ошибка при отправке сообщения модераторам");
            }
        }
    }
}