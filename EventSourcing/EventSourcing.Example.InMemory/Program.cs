﻿using EventSourcing;
using EventSourcing.Commands;
using EventSourcing.Example.Domain.Commands;
using EventSourcing.Example.Domain.Projections;
using EventSourcing.Example.JsonPayloads;
using EventSourcing.JsonPayloads;
using EventSourcing.Persistence.SqlStreamStore;
using FunicularSwitch;
using FunicularSwitch.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AccountCreated = EventSourcing.Example.Domain.Events.AccountCreated;

using var host = Host.CreateDefaultBuilder()
	.ConfigureServices(serviceCollection =>
	{
		var payloadAssemblies = typeof(AccountCreated).Assembly.Yield();
		var commandProcessorAssemblies = typeof(CreateAccountCommandProcessor).Assembly.Yield();
		var payloadMapperAssemblies = new []{typeof(AccountCreatedMapper), typeof(CommandProcessedMapper)}.Select(t => t.Assembly);

		serviceCollection
			.AddEventSourcing(payloadAssemblies, commandProcessorAssemblies, payloadMapperAssemblies)
			.AddSqlStreamEventStore();

		serviceCollection.AddSingleton<Accounts>();
		serviceCollection.AddTransient<SampleApp>();
	})
	.ConfigureLogging(builder => builder.AddConsole())
	.Build();

host.Services.GetRequiredService<Accounts>().AppliedEventStream.Subscribe(t =>
	Console.WriteLine($"Balance of {t.projection.Owner}s account changed: {t.projection.Balance}"));
host.Services.UseEventSourcing();

var output = await host.Services.GetRequiredService<SampleApp>()
	.CreateAccountsAndTransferMoney()
	.Match(
		ok => "Money transferred",
		error => $"Something went wrong: {error}"
	);
Console.WriteLine(output);

Console.ReadKey();

class SampleApp
{
	readonly Func<Command, Task<EventSourcing.OperationResult<Unit>>> _executeCommandAndWait;

	public SampleApp(Accounts accounts, ExecuteCommandAndWaitUntilApplied executeCommandAndWait)
	{
		_executeCommandAndWait = command => executeCommandAndWait(command, accounts.CommandProcessedStream);
	}

	public async Task<EventSourcing.OperationResult<Unit>> CreateAccountsAndTransferMoney()
	{
		var myAccount = Guid.NewGuid().ToString();
		var yourAccount = Guid.NewGuid().ToString();
		var results = await Task.WhenAll(
			_executeCommandAndWait(new CreateAccount(myAccount, "Alex", 0)),
			_executeCommandAndWait(new CreateAccount(yourAccount, "Mace", 1000))
		);
		return await results
			.Aggregate()
			.Bind(_ => _executeCommandAndWait(new TransferMoney(myAccount, yourAccount, 123)));
	}
}