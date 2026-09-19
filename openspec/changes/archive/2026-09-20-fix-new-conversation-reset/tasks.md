# Tasks

## 1. Reproduce

- [x] 1.1 Add a failing regression test to `web/src/chat/ChatPage.history.test.tsx`: render the page on `/chat/{id}` with the conversation answered (so the query cache holds it), click "New conversation", and assert the URL stays `/chat`, the chat is empty and the previous turns are gone; verify it fails against the current code for the reason in the proposal, not by accident

## 2. Fix

- [x] 2.1 Put a conversation's id into the URL only when that conversation was created by sending a message (a flag set on submit and cleared once the URL carries the id), instead of whenever the state holds an id the route does not; verify the regression test passes and the existing "URL appears after the first answer" behaviour still holds
- [x] 2.2 Do not hydrate a conversation while the route is moving away from it (`startNew` marks the route as leaving; hydrate only when the route still points at that conversation); verify no re-hydrate happens with a primed cache, and that opening a conversation from the sidebar and reloading on `/chat/{id}` both still hydrate

## 3. Verify

- [x] 3.1 In the running stack on :7171: open a stored conversation, click "New conversation", and confirm the URL stays `/chat` with an empty chat and no history rewrite (`push /chat` with no `replace` back); send a message and confirm the URL becomes `/chat/{new id}` and the conversation appears at the top of the list; repeat after a reload on `/chat/{id}` and with the sidebar collapsed. Run `make test` and `make lint`
