Signaling Messages
===========================================================

These 2 fields are always there

-   *Full User id (with device id)*

-   *Message Type*

 

SignalR sends it as **SIGNAL** message:

-   **Full User id**

-   **Message Type** (I_AM_HERE, HELLO, etc.)

-   *Message Payload*

 

Client code connection is configured as:

 

\_signalRConnection.On\<string, string, string\>("**SIGNAL**",

(string senderId, string messageType, string messagePayload) =\>

{

});

 

**Messages (message types):**

 

**I_AM_HERE** (send it first time when connected or re-connected)

*User id*

*Payload:*

-   **Timeout** (if not received any message from me within *Timeout* seconds,
    consider me as offline)

 

**HELLO** (send it back to I_AM_HERE)

*User id*

*Payload:*

-   **Timeout** (if not received any message from me within *Timeout* seconds,
    consider me as offline)

-   **Status (ONLINE, OFFLINE, BUSY, AWAY, DONOTDISTURB)**

(same as **I_AM_HERE**)

 

**STILL_HERE** (broadcast it every \~Timeout\*0.9 seconds)

*User id*

*Payload:*

-   **Status (ONLINE, OFFLINE, BUSY, AWAY, DONOTDISTURB)**

>    

**I_AM_OUTTA_HERE** (signing out on a device)

*User id*

 

*Custom types (examples)*

 

**WEBRTC** (WebRTC SDP/ICE, must be sent to a single user+device)

*User id*

*Payload:*

-   **SDP/ICE message**

 

**MSGBOX** (plain text messages, for debugging purposes)

*User id*

*Payload:*

-   **Message**

 
