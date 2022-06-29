﻿using EventSourcing.Commands;
using EventSourcing.Example.Domain.Events;

namespace EventSourcing.Example.Domain.Commands;

public record CreateAccount(string AccountId, string Owner, decimal InitialBalance) : Command;

public class CreateAccountCommandProcessor : SynchronousCommandProcessor<CreateAccount>
{
	public override ProcessingResult.Processed_ InternalProcessSync(CreateAccount command) => command.ToProcessedResult(
		new AccountCreated(command.AccountId, command.Owner, command.InitialBalance),
		FunctionalResult.Ok($"Account {command.AccountId} create for {command.Owner}"));
}