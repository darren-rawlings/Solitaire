using System.Collections.Generic;
using Renegade.Ui;

namespace Solitaire.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("The goal",
			"Get all 52 cards up onto the four foundations, top right: each one a suit, from the ace up to the king."),
		new TutorialPage("The piles",
			"On the seven piles, build down in alternating colours: a red six on a black seven. Tap a card to send it, and any cards on it, to the best place it can go. Only a king fills an empty pile."),
		new TutorialPage("The stock",
			"Tap the stock, top left, to turn cards onto the waste; tap the waste's top card to play it. When the stock runs out, tap its place to turn the waste back over. Settings › Draw chooses one card or three at a time."),
		new TutorialPage("Help",
			"Undo is free and goes back as far as you like. Hint shows a good move. Safe cards go up by themselves, and once every card is face up the game finishes itself. Settings › Deals can give you only winnable deals."),
	};
}
