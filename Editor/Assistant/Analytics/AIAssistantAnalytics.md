# AI Assistant Analytics Events

### UI Trigger Backend Event

| SubType                        | Description                                                                                         |
|--------------------------------|-----------------------------------------------------------------------------------------------------|
| `favorite_conversation`        | Marks a conversation as favorite or not. Includes ConversationId, IsFavorite and ConversationTitle. |
| `delete_conversation`          | Deletes a previous conversation. Includes ConversationId and ConversationTitle.                     |
| `rename_conversation`          | Renames a conversation. Includes ConversationId and ConversationTitle.                              |
| `load_conversation`            | Loads a previously conversation. Includes ConversationId and ConversationTitle.                     |
| `cancel_request`               | Cancels a message request. Includes ConversationId.                                                 |
| `edit_code`                    | User edited the run command script.                                                                 |
| `create_new_conversation`      | User started a new conversation.                                                                    |
| `refresh_inspirational_prompt` | User refreshed inspirational prompt.                                                                |

---

### Context Events

| SubType                                    | Description                                                                                  |
|--------------------------------------------|----------------------------------------------------------------------------------------------|
| `expand_context`                           | User expanded the attached context section.                                                  |
| `expand_command_logic`                     | User expanded the command logic section.                                                     |
| `ping_attached_context_object_from_flyout` | User pinged a context object from the flyout. Includes ContextType and ContextContent.       |
| `clear_all_attached_context`               | Cleared all attached context items.                                                          |
| `remove_single_attached_context`           | Removed a single attached context item.  Includes ContextType and ContextContent.            |
| `drag_drop_attached_context`               | Dragged and dropped a context object. Includes ContextType, ContextContent and IsSuccessful. |
| `choose_context_from_flyout`               | User chose a context object from the flyout. Includes ContextType and ContextContent.        |

---

### Plugin Events

| SubType       | Description                                  |
|---------------|----------------------------------------------|
| `call_plugin` | User invoked a plugin. Includes PluginLabel. |

---

### UI Trigger Local Event

| SubType                                         | Description                                                                                                   |
|-------------------------------------------------|---------------------------------------------------------------------------------------------------------------|
| `open_shortcuts`                                | Opened the shortcuts panel.                                                                                   |
| `execute_run_command`                           | Ran a command from the UI. Includes MessageId, ConversationId and ResponseMessage                             |
| `use_inspirational_prompt`                      | User clicked an inspirational prompt. Includes UsedInspirationalPrompt.                                       |
| `choose_mode_from_shortcut`                     | User chose a shortcut mode. Includes ChosenMode.                                                              |
| `copy_code`                                     | User copied code from a run command response. Includes ConversationId and ResponseMessage.                    |
| `copy_response`                                 | User copied a response message from any command type. Includes ConversationId, MessageId and ResponseMessage. |
| `save_code`                                     | User saved a response message. Includes ResponseMessage.                                                      |
| `open_reference_url`                            | User clicked on a reference URL. Includes Url.                                                                |
| `modify_run_command_preview_with_object_picker` | User clicked on a reference URL. Includes PreviewParameter.                                                   |
| `modify_run_command_preview_value`              | User clicked on a reference URL. Includes PreviewParameter.                                                   |
| `permission_response`                           | User responded to a permission request. Includes ConversationId, FunctionId, UserAnswer and PermissionType.   |

---
---

## Field Schema Details

### Common Fields

| Field Name | Type   | Description                                             |
|------------|--------|---------------------------------------------------------|
| `SubType`  | string | Describes the specific type of action within the group. |

---

### UITriggerBackendEventData

| Field Name          | Type   | Description                                           |
|---------------------|--------|-------------------------------------------------------|
| `SubType`           | string | Specific subtype like 'cancel_request', etc.          |
| `ConversationId`    | string | ID of the conversation where the event occurred.      |
| `MessageId`         | string | ID of the message involved in the event.              |
| `ResponseMessage`   | string | The actual message text (if applicable).              |
| `ConversationTitle` | string | Title of the conversation.                            |
| `IsFavorite`        | string | Indicates if the conversation was marked as favorite. |

---

### ContextEventData

| Field Name       | Type   | Description                                     |
|------------------|--------|-------------------------------------------------|
| `SubType`        | string | Subtype like 'drag_drop_attached_context', etc. |
| `ContextContent` | string | Name of the context object.                     |
| `IsSuccessful`   | string | Whether the context interaction succeeded.      |
| `ContextType`    | string | Type of the context object.                     |

---

### PluginEventData

| Field Name    | Type   | Description                                 |
|---------------|--------|---------------------------------------------|
| `SubType`     | string | Subtype such as 'call_plugin', etc.         |
| `PluginLabel` | string | Identifier or label for the plugin invoked. |

---

### UITriggerLocalEventData

| Field Name                | Type   | Description                                            |
|---------------------------|--------|--------------------------------------------------------|
| `SubType`                 | string | Subtype such as 'open_shortcuts', etc.                 |
| `UsedInspirationalPrompt` | string | Inspirational prompt being used.                       |
| `ChosenMode`              | string | Selected mode, such as run or ask mode.                |
| `ReferenceUrl`            | string | URL of the reference being called.                     |
| `ConversationId`          | string | ID of the conversation where the event occurred.       |
| `MessageId`               | string | ID of the message involved in the event.               |
| `ResponseMessage`         | string | Message content involved in the UI action.             |
| `PreviewParameter`        | string | Run command preview parameter after user modification. |
| `FunctionId`              | string | ID of the function/tool that requested the permission. |
| `UserAnswer`              | string | User's response: "AllowOnce", "AllowAlways", or "DenyOnce". |
| `PermissionType`          | string | Category of permission: e.g. "ToolExecution", "FileSystem", "CodeExecution", etc. |

---

### SpecialCharInUserMessageEventData

| Field Name       | Type   | Description                                          |
|------------------|--------|------------------------------------------------------|
| `userPrompt`     | string | The user-entered prompt that triggered the event.    |
| `commandMode`    | string | The mode the assistant was in when command was sent. |
| `conversationId` | string | Associated conversation ID.                          |
