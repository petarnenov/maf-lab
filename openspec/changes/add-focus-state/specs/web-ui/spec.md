# Spec Delta

## ADDED Requirements

### Requirement: The account in focus is shown and can be changed
**The chip.**
- When the conversation has an account in focus, the chat SHALL show it in a chip above the message box.
- The chip SHALL say it is the account in focus, in the language of the page's last question.
- The chip SHALL offer ✕ to clear the focus.
- Without a focus there SHALL be no chip.

**Choosing an account.** Each row of an accounts card, and each holdings or AUM card, SHALL offer "Focus". It sets that
account as the one in focus.

**Sending the state.**
- A change made in the UI SHALL be sent with the next message as the run's state. It SHALL NOT start a run of its own.
- The chip SHALL follow the state the server sends back, so a focus the server refused does not stay on screen.

**Restoring.** Reopening a conversation SHALL restore its account in focus.

#### Scenario: Focus from a read
- **WHEN** a turn reads A-1043's portfolio
- **THEN** the chip shows A-1043 once the turn's state arrives

#### Scenario: Focus from an accounts card
- **WHEN** the user presses "Focus" on A-1044 in an accounts card and then asks "What does it hold?"
- **THEN** the request carries `state: { focus: { accountId: "A-1044" } }`, and the chip shows A-1044

#### Scenario: Clearing
- **WHEN** the user presses ✕ on the chip and sends a message
- **THEN** the request carries `state: { focus: null }`, and no chip is shown

#### Scenario: A refused focus
- **WHEN** the server's starting snapshot names a different account than the one the client sent
- **THEN** the chip shows the server's account
