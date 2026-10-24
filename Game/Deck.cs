using System;
using System.Collections.Generic;
using System.IO;
using YGOSharp.OCGWrapper;

namespace WindBot.Game
{
    public class Deck
    {
        public IList<NamedCard> Cards { get; private set; }
        public IList<NamedCard> ExtraCards { get; private set; }
        public IList<NamedCard> SideCards { get; private set; }

        public Deck()
        {
            Cards = new List<NamedCard>();
            ExtraCards = new List<NamedCard>();
            SideCards = new List<NamedCard>();
        }

        private bool AddNewCard(int cardId, bool sideDeck)
        {
            NamedCard newCard = NamedCard.Get(cardId);
            if (newCard == null)
                return false;

            if (!sideDeck)
                AddCard(newCard);
            else
                SideCards.Add(newCard);
            return true;
        }

        private void AddCard(NamedCard card)
        {
            if (card.IsExtraCard())
                ExtraCards.Add(card);
            else
                Cards.Add(card);
        }

        public static Deck Load(string name)
        {
            StreamReader reader = null;
            try
            {
                reader = new StreamReader(Program.ReadFile("Decks", name, "ydk"));

                Deck deck = new Deck();
                bool side = false;

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                        continue;

                    line = line.Trim();
                    if (line.StartsWith("#"))
                        continue;
                    if (line.Equals("!side"))
                    {
                        side = true;
                        continue;
                    }

                    int id;
                    if (!int.TryParse(line, out id))
                        continue;

                    if (!deck.AddNewCard(id, side))
                        Logger.WriteLine("Deck \"" + name + "\" skipped unknown card id " + id + (side ? " (side)" : ""));
                }

                string path = name;
                if (reader.BaseStream is FileStream file)
                    path = file.Name;
                reader.Close();

                if (deck.Cards.Count > 60 || deck.ExtraCards.Count > 15 || deck.SideCards.Count > 15)
                {
                    Logger.WriteErrorLine("Deck \"" + path + "\" exceeds the client limit: main="
                        + deck.Cards.Count + " extra=" + deck.ExtraCards.Count
                        + " side=" + deck.SideCards.Count + ".", null);
                    return null;
                }

                return deck;
            }
            catch (Exception ex)
            {
                reader?.Close();
                Logger.WriteErrorLine("Failed to load deck \"" + name + "\".", ex);
                return null;
            }
        }
    }
}