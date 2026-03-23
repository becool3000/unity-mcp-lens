using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    class UnsafeCommandApprovalInteraction : BaseInteraction<bool>
    {
        public UnsafeCommandApprovalInteraction()
        {
            style.flexDirection = FlexDirection.Column;
            style.paddingTop = 8;
            style.paddingBottom = 8;

            var warningLabel = new Label("This command performs non-revertable actions. Are you sure you want to proceed?")
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 8
                }
            };
            Add(warningLabel);

            var buttonContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row
                }
            };

            var proceedButton = new Button(() => CompleteInteraction(true))
            {
                text = "Proceed"
            };
            buttonContainer.Add(proceedButton);

            var cancelButton = new Button(() => CompleteInteraction(false))
            {
                text = "Cancel"
            };
            buttonContainer.Add(cancelButton);

            Add(buttonContainer);
        }
    }
}
