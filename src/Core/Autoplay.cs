using System;
using System.Collections.Generic;
using Renegade.Cards;

namespace Solitaire.Core;

/// <summary>The computer playing Klondike its way (<see cref="Klondike.Suggest"/>): for the demo, and for finding winnable deals.</summary>
public static class Autoplay
{
	/// <summary>The computer's next step: a move, or null to turn the stock.</summary>
	public static Move? Next(Klondike game) => game.CanFinish ? game.FinishingMove() : game.Suggest();

	/// <summary>Plays a deal to the end; returns whether it was won.</summary>
	public static bool Play(Klondike game, int limit = 2000)
	{
		for (int n = 0; n < limit && !game.IsWon; n++)
		{
			if (Next(game) is { } move)
				game.Make(move);
			else if (game.Stuck || !game.Draw())
				return false;
		}
		return game.IsWon;
	}

	/// <summary>A shuffle the computer can win (so it's surely winnable), or any shuffle if none turns up.</summary>
	public static List<Card> WinnableDeal(Random random, int drawCount, int tries = 300)
	{
		var deck = Deck.Shuffled(random);
		for (int i = 0; i < tries && !Play(new Klondike(deck, drawCount)); i++)
			deck = Deck.Shuffled(random);
		return deck;
	}
}
