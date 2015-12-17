﻿/*
 * Copyright (c) Dominick Baier, Brock Allen.  All rights reserved.
 * see license
 */

(function () {
    var httpRequest;
    function alertContents() {
        if (httpRequest.readyState === XMLHttpRequest.DONE) {
            document.forms[0].submit();
        }
    }

    function writeToLogAndSendData() {
        var url = "/identity/FinishAuth";

        httpRequest = new XMLHttpRequest();

        if (!httpRequest) {
            alert('Giving up :( Cannot create an XMLHTTP instance');
            return false;
        }
        httpRequest.onreadystatechange = alertContents;
        httpRequest.open('GET', url);
        httpRequest.send();
    }

    function submitResult(needSetSsoCookieInput, timerReplace) {
        if (needSetSsoCookieInput != null) {
            needSetSsoCookieInput.parentNode.removeChild(needSetSsoCookieInput);
        }

        clearTimeout(timerReplace);

        writeToLogAndSendData();
    }

    var needSetSsoCookieInput = document.getElementsByName("need_set_sso_cookie")[0];

    var timerReplace = setTimeout(function () {
        submitResult(needSetSsoCookieInput, timerReplace);
    }, 10000);

    var needSetSsoCookie = needSetSsoCookieInput != null && needSetSsoCookieInput.value === "True";
    if (needSetSsoCookie) {
        function listener(event) {
            if (event.origin !== window.location.origin) {
                return;
            }

            if (event.data === "finishedLoadedProtectedResources") {
                submitResult(needSetSsoCookieInput, timerReplace);
            }
        }

        if (window.addEventListener) {
            window.addEventListener("message", listener);
        } else {
            window.attachEvent("onmessage", listener);
        }

        var iframe = document.createElement('iframe');
        iframe.id = "processSsoIframe";
        iframe.setAttribute("src", "/identity/ProcessSso");
        iframe.setAttribute("style", "display: none;");
        document.body.appendChild(iframe);
    } else {
        submitResult(needSetSsoCookieInput, timerReplace);
    }
})();
