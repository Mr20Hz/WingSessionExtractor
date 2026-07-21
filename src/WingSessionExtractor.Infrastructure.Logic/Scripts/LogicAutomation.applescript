on run arguments
    set operationName to item 1 of arguments

    if operationName is "check-accessibility" then
        tell application "System Events" to return UI elements enabled
    end if

    if operationName is "process-running" then
        tell application "System Events" to return exists process "Logic Pro"
    end if

    tell application "System Events"
        if not (exists process "Logic Pro") then return false
        tell process "Logic Pro"
            if operationName is "project-open" then
                return (count of windows) > 0
            end if

            if operationName is "open-import-dialog" then
                set frontmost to true
                click menu item "Audio File…" of menu 1 of menu item "Import" of menu "File" of menu bar 1
                return true
            end if

            if operationName is "import-dialog-open" then
                if (count of windows) is 0 then return false
                return (count of sheets of window 1) > 0
            end if

            if operationName is "show-file-location-dialog" then
                keystroke "g" using {command down, shift down}
                return true
            end if

            if operationName is "file-location-dialog-open" then
                if (count of windows) is 0 then return false
                if (count of sheets of window 1) is 0 then return false
                return (count of sheets of sheet 1 of window 1) > 0
            end if

            if operationName is "choose-import-file" then
                set trackPath to item 2 of arguments
                set locationDialog to sheet 1 of sheet 1 of window 1
                set value of text field 1 of locationDialog to trackPath
                click button "Go" of locationDialog
                return true
            end if

            if operationName is "confirm-import-file" then
                click button "Open" of sheet 1 of window 1
                return true
            end if

            if operationName is "save-project" then
                set frontmost to true
                keystroke "s" using {command down}
                return true
            end if

            if operationName is "project-saved" then
                if (count of windows) is 0 then return false
                try
                    return not (value of attribute "AXDocumentModified" of window 1)
                on error
                    return false
                end try
            end if
        end tell
    end tell

    error "Unknown Logic automation operation: " & operationName
end run
