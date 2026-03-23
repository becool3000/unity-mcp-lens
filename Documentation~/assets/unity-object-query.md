---
uid: assistant-object-query
---

# Interact with GameObjects, components, assets, and console messages

Use Assistant to interact contextually with GameObjects, components, assets, and console messages directly within the Unity Editor. The **Attach** window lets you to attach relevant objects to your query, retrieve information, and perform actions on them.

After you attach objects, you can ask Assistant to analyze, modify, or manipulate them. For example, you can do the following tasks:

* Retrieve information about a GameObject’s properties and components.
* Query asset metadata, references, or debugging details.
* Investigate and troubleshoot console logs and errors.
* Modify or duplicate objects in the scene. For instance, you can attach multiple tree prefabs and instruct Assistant to scatter them randomly throughout the scene in different sizes and placements.

The following section explains how to attach and interact with different Unity objects using the **Attach** window.

## Engage with your development environment

The **Attach** window lets you attach, query, and manage Unity objects.

### Interact with GameObjects

Assistant can answer queries about the properties, components, or usage of the attached GameObjects.

To use the **Attach Window** to query GameObjects, follow these steps:

1. In the **Scene** View or **Hierarchy** Window, select one or more GameObjects.
1. In the **Assistant** window, select **Attach**.
1. Attach the objects through one of the following methods:

   * Search for GameObjects in the **Hierarchy** tab.
   * Drag the selected GameObjects to the **Attach** window.

### Interact with assets

You can query assets for details like metadata, references, or debugging information.

To use the **Attach Window** to query assets, follow these steps:

1. In the **Project** window, select one or more assets such as prefabs, textures, or materials.
1. In the **Assistant** window, select **Attach**.
1. Attach the objects through one of the following methods:

   * Search for assets in the **Project** tab.
   * Drag the assets to the **Attach** window.

### Interact with console logs and errors

Assistant analyzes the attached logs or errors to debug context.

To use the **Attach Window** to query console messages, follow these steps:

1. To open the **Console** window, in the main menu, go to **Window** > **Panels** > **Console**.
1. In the **Console** window, select one or more messages, then select **Attach** next to each message.

## Use the Attach window

The **Attach** window has the following tabs:

| Tab | Description |
| --- | ----------- |
| **All** | Displays all the selected objects (GameObjects, assets, console logs) across the editor. It requires a search input to display results. |
| **Project** | Filters results to objects from the **Project** window, for example, assets or prefabs. It requires a search input to display results. You can also drag objects to this tab. |
| **Hierarchy** | Filters results to objects from the **Hierarchy** window, for example, GameObjects in your active scene. It requires a search input to display results. You can also drag objects to this tab. |
| **Selection** | Displays a static, unfiltered list of objects currently selected in the editor. This tab doesn't filter results based on the search input. |

> [!NOTE]
> If you select a mix of GameObjects, assets, components, and console logs, the **Attach** window prioritizes the selected console logs and lists them at the top of the search results, with the remaining selected objects listed below.

### Key features of the Attach window

The **Attach** window provides the following features.

#### Search function

Search across **All**, **Project**, and **Hierarchy** tabs to locate and attach relevant objects. The selected console logs appear at the top of the results.

#### Find option

The **Attach** window displays a **Find** button next to each result. When you select **Find**, it highlights the corresponding object in the **Hierarchy** or **Project** window.

> [!NOTE]
> The **Find** option is available for all objects except console logs.

#### Dropdown for excess attachments

If you attach too many objects, the **Attach** window converts into a dropdown menu to optimize space.

> [!TIP]
> To maintain accuracy and performance, limit the number of attached objects.

#### Clear content

Use **Clear all** to remove all the currently attached objects. To remove individual objects, select **Remove** next to them.

#### Drag option

Drag GameObjects, assets, or console logs directly into the **Attach** Window for attachment.

## Additional resources

* [Open and use Assistant](xref:get-started)
* [Use Ask mode and Agent mode](xref:assistant-modes)