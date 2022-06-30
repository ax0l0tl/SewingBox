using EventSourcing;
using Example.Domain.Events;
using Example.Domain.Projections;
using FunicularSwitch;
using FunicularSwitch.Extensions;
using JetBrains.Annotations;

namespace Example.Host.GraphQl;

[UsedImplicitly]
public class Query
{
	public async Task<IEnumerable<Account>> GetAccounts([Service] LoadAllEvents loadEvents,
		[Service] Accounts accountCache)
	{
		var events = await loadEvents();
		var accountsWithInitialBalance = await
			events
				.Select(e => e.Payload)
				.OfType<AccountCreated>()
				.SelectAsync(accountCreated => accountCache
					.Get(accountCreated.StreamId)
					.Map(account => new Account(account, accountCreated.InitialBalance)));

		return accountsWithInitialBalance.Choose(a => a);
	}
}

public record Account
{
	public Account(Example.Domain.Projections.Account inner, decimal initialBalance)
	{
		Inner = inner;
		InitialBalance = initialBalance;
	}

	[GraphQLIgnore]
	internal Example.Domain.Projections.Account Inner { get; }
	public decimal InitialBalance { get; }
	public string Id => Inner.Id;
	public string Owner => Inner.Owner;
	public decimal Balance => Inner.Balance;
}