 "use strict";
(function () {
    // The HTML for this View
    var viewHTML;
    var scope = [window.config.clientID];

    // Calls the calendarList Web API with an HTTP Bearer access request, and update data
    function getCalendar(accessToken, dataContainer, loading) {
        // Get calendarList Data
        $.ajax({
            type: "GET",
            url: "/api/calendar",
            headers: {
                'Authorization': 'Bearer ' + accessToken,
            },
        }).done(function (data) {

            var $html = $(viewHTML);
            var $template = $html.find(".data-container");

            // For Each calendar Item Returned, Append a Table Row
            var output = data.reduce(function (rows, calendarItem, index, calendars) {
                var $entry = $template;
                var $description = $entry.find(".view-data-description").html(calendarItem.subject);
                $entry.find(".data-template").attr('data-calendar-id', calendarItem.id);
                return rows + $entry.html();
            }, '');

            // Update the UI
            loading.hide();
            dataContainer.html(output);

        }).fail(function (jqXHR, textStatus) {
            printErrorMessage('Error getting calendar list data')
        }).always(function () {

            // Register Handlers for Buttons in Data Table
            registerDataClickHandlers();
        });
    }


    // Calls the Calendar Web API with an HTTP Bearer access request, and deletes a calendar item
    function deleteCalendarItem(accessToken, calendarId) {
        // Delete the Calendar
        $.ajax({
            type: "DELETE",
            url: "/api/Calendar/" + calendarId,
            headers: {
                'Authorization': 'Bearer ' + accessToken,
            },
        }).done(function () {
            console.log('DELETE success.');
        }).fail(function () {
            console.log('Fail on new Calendar DELETE');
            printErrorMessage('Error deleting calendar item.')
        }).always(function () {
            refreshViewData();
        });
    }

    // Calls the Calendar Web API with an HTTP Bearer acess request, and saves a calendar item
    function saveCalendarItem(accessToken, calendarId, description) {
        // Update Calendar Item
        $.ajax({
            type: "PUT",
            url: "/api/Calendar",
            headers: {
                'Authorization': 'Bearer ' + accessToken,
            },
            data: {
                Description: description.val(),
                ID: calendarId,
            },
        }).done(function () {
            console.log('PUT success.');
        }).fail(function () {
            console.log('Fail on calendar PUT');
            printErrorMessage('Error saving calendar item.')
        }).always(function () {
            refreshViewData();
            description.val('');
        });
    }

    function refreshViewData() {

        // Empty Old View Contents
        var $dataContainer = $(".data-container");
        $dataContainer.empty();
        var $loading = $(".view-loading");

        // Get the access token for the backend, and calls the Web API to refresh the calendar list
        clientApplication.acquireTokenSilent(scope)
            .then(function (token) {
                getCalendar(token, $dataContainer, $loading);
            }, function (error) {
                clientApplication.acquireTokenPopup(scope).then(function (token) {
                    getCalendar(token, $dataContainer, $loading);
                }, function (error) {
                    printErrorMessage(error);
                });
            })

    }


    function registerDataClickHandlers() {

        // Delete Button(s)
        $(".view-data-delete").click(function (event) {
            clearErrorMessage();

            var calendarId = $(event.target).parents(".data-template").attr("data-calendar-id");

            // Get the access token for the backend, and calls the Web API to delete the current item
            clientApplication.acquireTokenSilent(scope)
                .then(function (token) {
                    deleteCalendarItem(token, calendarId);
                }, function (error) {
                    clientApplication.acquireTokenPopup(scope).then(function (token) {
                        deleteCalendarItem(token, calendarId);
                    }, function (error) {
                        printErrorMessage(error);
                    });
                })

        });

        // Edit Button(s)
        $(".view-data-edit").click(function (event) {
            clearErrorMessage();
            var $entry = $(event.target).parents(".data-template");
            var $entryDescription = $entry.find(".view-data-description").hide();
            var $editInput = $entry.find(".view-data-edit-input");
            $editInput.val($entryDescription.text());
            $editInput.show();
            $entry.find(".view-data-mode-delete").hide();
            $entry.find(".view-data-mode-edit").show();
        });

        // Cancel Button(s)
        $(".view-data-cancel-edit").click(function (event) {
            clearErrorMessage();
            $entry = $(event.target).parents(".data-template");
            $entry.find(".view-data-description").show();
            $editInput = $entry.find(".view-data-edit-input").hide();
            $editInput.val('');
            $entry.find(".view-data-mode-delete").show();
            $entry.find(".view-data-mode-edit").hide();
        });

        // Save Button(s)
        $(".view-data-save").click(function (event) {
            clearErrorMessage();
            var $entry = $(event.target).parents(".data-template");
            var calendarId = $entry.attr("data-calendar-id");

            // Validate Calendar Description
            var $description = $entry.find(".view-data-edit-input");
            if ($description.val().length <= 0) {
                printErrorMessage('Please enter a valid Calendar description');
                return;
            }

            // Get the access token for the backend, and calls the Web API to save the current item
            clientApplication.acquireTokenSilent(scope)
                .then(function (token) {
                    saveCalendarItem(token, calendarId, $description);
                }, function (error) {
                    clientApplication.acquireTokenPopup(scope).then(function (token) {
                        deleteCalendarItem(token, calendarId, $description);
                    }, function (error) {
                        printErrorMessage(error);
                    });
                })

        });
    };

    function postNewCalendar(accesstoken, description) {
        // POST a New Calendar
        $.ajax({
            type: "POST",
            url: "/api/Calendar",
            headers: {
                'Authorization': 'Bearer ' + accesstoken,
            },
            data: {
                Description: description.val(),
            },
        }).done(function () {
            console.log('POST success.');
        }).fail(function () {
            console.log('Fail on new Calendar POST');
            printErrorMessage('Error adding new calendar item.');
        }).always(function () {

            // Refresh Calendar
            description.val('');
            refreshViewData();
        });
    }

    function registerViewClickHandlers() {

        // Add Button
        $(".view-addCalendar").click(function () {
            clearErrorMessage();

            // Validate Calendar Description
            var $description = $("#view-calendarDescription");
            if ($description.val().length <= 0) {
                printErrorMessage('Please enter a valid Calendar description');
                return;
            }

            clientApplication.acquireTokenSilent(scope)
                .then(function (token) {
                    postNewCalendar(token, $description);
                }, function (error) {
                    clientApplication.acquireTokenPopup(scope).then(function (token) {
                        deleteCalendarItem(token, $description);
                    }, function (error) {
                        printErrorMessage(error);
                    });
                })

        });
    };

    function clearErrorMessage() {
        var $errorMessage = $(".app-error");
        $errorMessage.empty();
    };

    function printErrorMessage(mes) {
        var $errorMessage = $(".app-error");
        $errorMessage.html(mes);
    }

    // Module
    window.calendarCtrl = {
        requireADLogin: true,
        preProcess: function (html) {

        },
        postProcess: function (html) {
            viewHTML = html;
            registerViewClickHandlers();
            refreshViewData();
        },
    };
}());

