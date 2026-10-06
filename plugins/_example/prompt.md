<!-- summary -->
the sample fact the authoring template serves (a stand-in for a plugin's own data)
<!-- scope -->
the sample fact
<!-- tools -->
Sample:
- get_example_fact — the sample fact for the caller's tenant, for the question asked. Returns the tenant, the question and the fact.
<!-- examples -->
- "What is the example fact for this tenant?" → get_example_fact; answer with the fact it returns
<!-- rules -->
- Answer only from the fact the tool returns; never invent one.
