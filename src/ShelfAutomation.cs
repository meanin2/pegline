using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Pegline
{
    internal sealed class ShelfAutomationPeer : FrameworkElementAutomationPeer
    {
        readonly ShelfView shelf;
        readonly Dictionary<Guid, CardAutomationPeer> cards = new Dictionary<Guid, CardAutomationPeer>();
        public ShelfAutomationPeer(ShelfView shelf) : base(shelf) { this.shelf = shelf; }
        protected override string GetClassNameCore() { return "PeglineShelf"; }
        protected override string GetNameCore() { return Ui.L("Screenshot clothesline", "Tendedero de capturas"); }
        protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.List; }
        protected override List<AutomationPeer> GetChildrenCore()
        {
            var live = shelf.Host.Cards.Where(c => !c.Falling).ToArray();
            foreach (var id in cards.Keys.Where(id => !live.Any(c => c.Id == id)).ToArray()) cards.Remove(id);
            foreach (var card in live) if (!cards.ContainsKey(card.Id)) cards.Add(card.Id, new CardAutomationPeer(shelf, card));
            return live.Select(c => (AutomationPeer)cards[c.Id]).ToList();
        }
    }
    // Virtual cards do not share a FrameworkElement owner/identity. Cache peers
    // by card ID in the parent so their automation identity survives repaints.
    internal sealed class CardAutomationPeer : AutomationPeer, IInvokeProvider
    {
        readonly ShelfView shelf;
        readonly Card card;
        public CardAutomationPeer(ShelfView shelf, Card card) { this.shelf = shelf; this.card = card; }
        protected override string GetClassNameCore() { return "PeglineScreenshot"; }
        protected override string GetAutomationIdCore() { return card.Id.ToString("N"); }
        protected override string GetNameCore() { return Path.GetFileName(card.Path); }
        protected override string GetHelpTextCore() { return Ui.L("Invoke to copy. Open the screenshot library for keyboard editing and file actions.", "Invocar para copiar. Abre la biblioteca para editar y gestionar archivos con teclado."); }
        protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.Button; }
        protected override bool IsKeyboardFocusableCore() { return false; }
        protected override bool HasKeyboardFocusCore() { return false; }
        protected override bool IsEnabledCore() { return !card.Falling && shelf.Host.Cards.Contains(card); }
        protected override bool IsOffscreenCore() { return !shelf.TargetVisible || !IsEnabledCore() || card.Flying; }
        protected override bool IsContentElementCore() { return true; }
        protected override bool IsControlElementCore() { return true; }
        protected override bool IsPasswordCore() { return false; }
        protected override bool IsRequiredForFormCore() { return false; }
        protected override string GetAcceleratorKeyCore() { return ""; }
        protected override string GetAccessKeyCore() { return ""; }
        protected override string GetItemStatusCore() { return card.Flying ? Ui.L("Arriving", "Llegando") : ""; }
        protected override string GetItemTypeCore() { return Ui.L("Screenshot", "Captura"); }
        protected override AutomationOrientation GetOrientationCore() { return AutomationOrientation.None; }
        protected override AutomationPeer GetLabeledByCore() { return null; }
        protected override Rect GetBoundingRectangleCore() { return IsOffscreenCore() ? Rect.Empty : shelf.CardBounds(card); }
        protected override Point GetClickablePointCore()
        {
            var bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        }
        protected override List<AutomationPeer> GetChildrenCore() { return null; }
        protected override void SetFocusCore() { throw new InvalidOperationException("Use the screenshot library for keyboard focus."); }
        public override object GetPattern(PatternInterface pattern) { return pattern == PatternInterface.Invoke ? this : null; }
        void IInvokeProvider.Invoke()
        {
            if (!IsEnabledCore()) throw new ElementNotEnabledException();
            shelf.Dispatcher.BeginInvoke(new Action(delegate { if (IsEnabledCore()) shelf.Host.Copy(card); }));
        }
    }
}
