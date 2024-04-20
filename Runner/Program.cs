﻿using Grpc.Core;
using Grpc.Net.Client;
using PlayerInterface;
using Swoc2024;
using System.Text.Json;
using Client = PlayerInterface.PlayerHost.PlayerHostClient;

Console.WriteLine("Connecting...");
var client = GetClient();
Console.WriteLine("Connected");

Console.WriteLine("Registering...");
var settings = await RegisterAsync(client);
Console.WriteLine("Registered");

World world = new();

// _gameState = new GameState(settings.Dimensions.ToArray(), settings.StartAddress.ToArray(), playerName, settings.PlayerIdentifier);
_ = SubscribeServerEvents(client, world);

_ = SubscribeDelta(client, settings.PlayerIdentifier, world);

await SetupWorldAsync(client, world);

while(true)
{
    await Task.Delay(TimeSpan.FromSeconds(2));
    var snakes = world.GetSnakes().Where(i => i.Name == "Tommie").ToList();
    var jsonOpts = new JsonSerializerOptions()
    {
        WriteIndented = true,
    };

    //Console.WriteLine(JsonSerializer.Serialize(snakes.First().Positions.Take(2)));
    //Console.WriteLine(JsonSerializer.Serialize(snakes, new JsonSerializerOptions()
    //{
    //    WriteIndented = true,
    //}));
    Console.WriteLine(JsonSerializer.Serialize(world.GetFood().Count(), jsonOpts));
}

Client GetClient()
{
    var channel = GrpcChannel.ForAddress("http://192.168.178.62:5168");
    return new Client(channel);
}

async Task<GameSettings> RegisterAsync(Client client)
{
    byte[] buf = new byte[10];
    Random.Shared.NextBytes(buf);

    string playerName = args.Length > 0 ? args[0] : Convert.ToBase64String(buf);

    var register = new RegisterRequest
    {
        PlayerName = playerName,
    };
    Console.WriteLine($"Registering as: {playerName}.");
    var settings = await client.RegisterAsync(register);
    Console.WriteLine($"Registered, got ID: {settings.PlayerIdentifier}.");
    return settings;
}

Task SubscribeDelta(Client client, string playerid, World world, CancellationToken cancellation = default)
{
    var req = new SubsribeRequest
    {
        PlayerIdentifier = settings.PlayerIdentifier,
    };
    var deltaStream = client.Subscribe(req, cancellationToken: cancellation);
    Console.WriteLine("Subscribed");

    return Task.Factory.StartNew(async () =>
    {
        while(await deltaStream.ResponseStream.MoveNext())
        {
            var message = deltaStream.ResponseStream.Current;
            foreach(var cell in message.UpdatedCells)
            {
                world.QueueUpdate(new Cell(new Position([.. cell.Address]), cell.Player, cell.FoodValue > 0));
            }
        }
    });
}

Task SubscribeServerEvents(Client client, World world, CancellationToken cancellation = default)
{
    var stateChanges = client.SubscribeToServerEvents(new EmptyRequest { }, cancellationToken: cancellation);

    return Task.Factory.StartNew(async () =>
    {
        while (await stateChanges.ResponseStream.MoveNext())
        {
            var message = stateChanges.ResponseStream.Current;
            if (message.MessageType == MessageType.GameStateChange)
            {
                await Console.Out.WriteLineAsync($"Update: {message.Message}");

            }
            else
            {
                await Console.Out.WriteLineAsync($"PlayerJoined: {message.Message}");
            }
        }
    });
}

async Task SetupWorldAsync(Client client, World world, CancellationToken cancellation = default)
{
    var gameWorld = await client.GetGameStateAsync(new EmptyRequest(), cancellationToken: cancellation);

    world.StartWorld(
        gameWorld.UpdatedCells.Select(i => 
            new Cell(
                new Position([.. i.Address]),
                i.Player, 
                i.FoodValue > 0
            )
        )
    );
}