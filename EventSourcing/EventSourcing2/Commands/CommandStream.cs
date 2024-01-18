﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using EventSourcing.Commands;
using EventSourcing2.Events;
using EventSourcing2.Internal;
using FunicularSwitch;
using Microsoft.Extensions.Logging;
using AsyncLock = EventSourcing2.Internal.AsyncLock;
using Unit = FunicularSwitch.Unit;

namespace EventSourcing2.Commands;

public sealed class CommandStream : IObservable<Command>, IDisposable
{
	readonly AsyncLock _lock = new();
	readonly Subject<Command> _innerStream;
	readonly IObservable<Command> _commands;

	public CommandStream()
	{
		_innerStream = new();
		_commands = _innerStream.Publish().RefCount();
	}

	public IDisposable Subscribe(IObserver<Command> observer) => _commands.Subscribe(observer);

	public async Task SendCommand(Command command) => await _lock.ExecuteGuarded(() => _innerStream.OnNext(command)).ConfigureAwait(false);

	public Task SendCommands(IEnumerable<Command> commands) => Task.WhenAll(commands.Select(SendCommand));

	public void Dispose()
	{
		_lock.Dispose();
		_innerStream.Dispose();
	}
}

public static class CommandStreamExtension
{
	public static IDisposable SubscribeCommandProcessors(this IObservable<Command> commands, GetCommandProcessor getCommandProcessor, IEventStore writeEvents, ILogger logger, WakeUp? eventPollWakeUp) =>
		commands
			.Process(getCommandProcessor, writeEvents)
			.Do(r => r.LogResult(logger))
			//.Buffer(TimeSpan.FromMilliseconds(100))
			//.Where(l => l.Count > 0)
			.SubscribeAsync(async processingResult =>
			{
				//TODO: write command processed events only in faulted case and otherwise together with payloads produces by command processor
				try
				{
					var commandProcessedEvents = new List<EventPayload> { processingResult.ToCommandProcessedEvent() };
					await writeEvents.WriteEvents(commandProcessedEvents).ConfigureAwait(false);
					eventPollWakeUp?.ThereIsWorkToDo();
				}
				catch (Exception e)
				{
					logger.LogError(e, "Failed to persist command processed events");
				}
			}, logger);

	public static IObservable<ProcessingResult> Process(this IObservable<Command> commands,
		GetCommandProcessor getCommandProcessor, IEventStore writeEvents) =>
		commands
			.SelectMany(async c =>
			{
				var result = await CommandProcessor.Process(c, getCommandProcessor).ConfigureAwait(false);
				return await result.Match(
						processed: async p =>
						{
							try
							{
								await writeEvents.WriteEvents(p.ResultEvents).ConfigureAwait(false);
								return p;
							}
							catch (Exception e)
							{
								return new ProcessingResult.Faulted_(e, p.CommandId);
							}
						},
						cancelled: Task.FromResult<ProcessingResult>,
						faulted: Task.FromResult<ProcessingResult>,
						unhandled: Task.FromResult<ProcessingResult>)
					.ConfigureAwait(false);
			});

	public static async Task<OperationResult<Unit>> SendCommandAndWaitUntilApplied(this CommandStream commandStream,
		Command command, IObservable<CommandProcessed> commandProcessedEvents)
	{
		var processed = commandProcessedEvents
			.FirstAsync(c => c.CommandId == command.Id)
			.ToTask(CancellationToken.None, Scheduler.Default); //this is needed if we might be called from sync / async mixtures (https://blog.stephencleary.com/2012/12/dont-block-in-asynchronous-code.html)
		await commandStream.SendCommand(command).ConfigureAwait(false);

		return (await processed.ConfigureAwait(false)).OperationResult;
	}
}

public static class ProcessingResultExtension
{
	public static void LogResult(this ProcessingResult processingResult, ILogger? logger)
	{
		processingResult.Match(
			processed =>
			{
				if (processed.FunctionalResult is FunctionalResult.Failed_)
					logger?.LogError(processed.ResultMessage);
				logger?.LogInformation($"Processed command with id {processed.CommandId}. Resulting event count {processed.ResultEvents.Count}. FunctionalResult: {processed.FunctionalResult} Message: {processed.ResultMessage}");
			},
			unhandled => logger?.LogError($"Command was not handled {unhandled.CommandId}. Reason: {unhandled.ResultMessage}."),
			faulted => logger?.LogError(faulted.Exception, $"Error processing command with id {faulted.CommandId}."),
			cancelled => logger?.LogInformation($"Command with id {cancelled.CommandId} was cancelled")
		);
	}

	public static CommandProcessed ToCommandProcessedEvent(this ProcessingResult r)
	{
		var operationResult = ProcessingResultMatchExtension.Match(r, processed: p => p.FunctionalResult.Match(ok: _ => EventSourcing2.OperationResult.Ok(No.Thing), failed: failed => EventSourcing2.OperationResult.Error<Unit>(failed.Failure)),
			unhandled: u => EventSourcing2.OperationResult.InternalError<Unit>(u.ResultMessage ?? "No command processor registered"),
			faulted: f => EventSourcing2.OperationResult.InternalError<Unit>(f.ResultMessage ?? $"Command execution failed with exception: {f.Exception}"),
			cancelled: c => EventSourcing2.OperationResult.Cancelled<Unit>(c.ResultMessage));
		return new(r.CommandId, operationResult, r.ResultMessage);
	}

	public static void Match(this ProcessingResult processingResult, Action<ProcessingResult.Processed_> processed, Action<ProcessingResult.Unhandled_> unhandled, Action<ProcessingResult.Faulted_> faulted, Action<ProcessingResult.Cancelled_> cancelled)
	{
		static Func<T, int> ToFunc<T>(Action<T> action) => t => { action(t); return 42; };

		ProcessingResultMatchExtension.Match(processingResult, processed: p => ToFunc(processed)(p),
			unhandled: u => ToFunc(unhandled)(u),
			faulted: f => ToFunc(faulted)(f),
			cancelled: c => ToFunc(cancelled)(c));
	}
}