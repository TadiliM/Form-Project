import { describe, expect, it } from 'vitest';
import type { FieldResponse } from '../api/types';
import {
  boundToString,
  draftFromResponse,
  draftToPayload,
  emptyField,
  isFreePlanLimitError,
  validateAnswer,
  validateAnswers,
  validateFieldDraft,
  validateFormDraft,
} from './validation';

describe('validateFormDraft', () => {
  it('requires a title', () => {
    expect(validateFormDraft('   ', [emptyField()])).toBe('The form title is required.');
  });

  it('requires at least one field', () => {
    expect(validateFormDraft('My form', [])).toBe('A form must contain at least one field.');
  });

  it('accepts a valid draft', () => {
    expect(validateFormDraft('My form', [{ ...emptyField(), label: 'Name' }])).toBeNull();
  });
});

describe('validateFieldDraft', () => {
  it('requires a label', () => {
    expect(validateFieldDraft({ ...emptyField(), label: '  ' })).toBe(
      'Every field must have a label.',
    );
  });

  it('rejects a maxLength of zero', () => {
    expect(validateFieldDraft({ ...emptyField(), label: 'Name', maxLength: 0 })).toBe(
      'MaxLength must be greater than 0.',
    );
  });

  it('requires one non-empty option for a choice field', () => {
    expect(
      validateFieldDraft({ ...emptyField('choice'), label: 'Pick', options: ['  ', ''] }),
    ).toBe('A choice field must offer at least one option.');
  });

  it('rejects min greater than max', () => {
    expect(
      validateFieldDraft({ ...emptyField('number'), label: 'Age', min: '10', max: '5' }),
    ).toBe('Min cannot be greater than Max.');
  });

  it('accepts a number field with only a min', () => {
    expect(validateFieldDraft({ ...emptyField('number'), label: 'Age', min: '0', max: '' })).toBeNull();
  });
});

describe('validateAnswer', () => {
  const textField: FieldResponse = {
    id: 'f1',
    type: 'text',
    label: 'Name',
    isRequired: true,
    order: 0,
    maxLength: 10,
  };

  const choiceField: FieldResponse = {
    id: 'f2',
    type: 'choice',
    label: 'Colour',
    isRequired: false,
    order: 1,
    options: ['red', 'blue'],
  };

  const numberField: FieldResponse = {
    id: 'f3',
    type: 'number',
    label: 'Age',
    isRequired: false,
    order: 2,
    min: 0,
    max: 120,
  };

  it('rejects an empty required field', () => {
    expect(validateAnswer(textField, '')).toBe('Field "Name" is required.');
  });

  it('rejects text longer than maxLength', () => {
    expect(validateAnswer(textField, 'a'.repeat(11))).toBe('"Name" must be at most 10 characters.');
  });

  it('accepts an empty optional field', () => {
    expect(validateAnswer(choiceField, '')).toBeNull();
  });

  it('rejects a choice outside the options', () => {
    expect(validateAnswer(choiceField, 'green')).toBe('Choose a valid option for "Colour".');
  });

  it('rejects a number outside its bounds', () => {
    expect(validateAnswer(numberField, '200')).toBe('"Age" must be between 0 and 120.');
  });

  it('rejects a non-numeric value', () => {
    expect(validateAnswer(numberField, 'abc')).toBe('"Age" must be a number.');
  });

  it('accepts a valid number', () => {
    expect(validateAnswer(numberField, '42.5')).toBeNull();
  });
});

describe('validateAnswers', () => {
  it('returns the first error across the fields', () => {
    const fields: FieldResponse[] = [
      { id: 'f1', type: 'text', label: 'Name', isRequired: true, order: 0, maxLength: 500 },
      { id: 'f2', type: 'text', label: 'Note', isRequired: false, order: 1, maxLength: 500 },
    ];
    expect(validateAnswers(fields, { f2: 'hello' })).toBe('Field "Name" is required.');
    expect(validateAnswers(fields, { f1: 'Ada', f2: 'hello' })).toBeNull();
  });
});

describe('draftToPayload', () => {
  it('keeps only the properties of a text field', () => {
    const payload = draftToPayload({ ...emptyField('text'), label: '  Name  ', maxLength: 50 });
    expect(payload).toEqual({
      type: 'text',
      label: 'Name',
      isRequired: false,
      maxLength: 50,
      options: null,
      min: null,
      max: null,
    });
  });

  it('drops blank options of a choice field', () => {
    const payload = draftToPayload({
      ...emptyField('choice'),
      label: 'Colour',
      options: ['red', '  ', 'blue'],
    });
    expect(payload.options).toEqual(['red', 'blue']);
  });

  it('converts min and max to numbers for a number field', () => {
    const payload = draftToPayload({ ...emptyField('number'), label: 'Age', min: '1', max: '9' });
    expect(payload.min).toBe(1);
    expect(payload.max).toBe(9);
  });

  it('sends null bounds when left empty', () => {
    const payload = draftToPayload({ ...emptyField('number'), label: 'Age' });
    expect(payload.min).toBeNull();
    expect(payload.max).toBeNull();
  });
});

describe('boundToString', () => {
  it('formats a real bound', () => {
    expect(boundToString(0)).toBe('0');
    expect(boundToString(120)).toBe('120');
  });

  it('treats the decimal.MaxValue sentinel as no bound', () => {
    // The backend defaults Max to decimal.MaxValue, serialized as ~7.9e28.
    expect(boundToString(7.922816251426434e28)).toBe('');
  });

  it('treats null and undefined as no bound', () => {
    expect(boundToString(null)).toBe('');
    expect(boundToString(undefined)).toBe('');
  });
});

describe('draftFromResponse', () => {
  it('hides the decimal.MaxValue sentinel of a number field', () => {
    const draft = draftFromResponse({
      id: 'f1',
      type: 'number',
      label: 'Age',
      isRequired: false,
      order: 0,
      min: 0,
      max: 7.922816251426434e28,
    });

    expect(draft.min).toBe('0');
    expect(draft.max).toBe('');
  });
});

describe('validateAnswer with the max sentinel', () => {
  it('accepts a large number when max is decimal.MaxValue', () => {
    const field: FieldResponse = {
      id: 'f1',
      type: 'number',
      label: 'Age',
      isRequired: false,
      order: 0,
      min: 0,
      max: 7.922816251426434e28,
    };

    expect(validateAnswer(field, '999999')).toBeNull();
  });
});

describe('isFreePlanLimitError', () => {
  it('detects the plan limit message', () => {
    expect(
      isFreePlanLimitError('Free plan limit reached (3 forms). Upgrade to the Pro plan to create more.'),
    ).toBe(true);
  });

  it('ignores other messages', () => {
    expect(isFreePlanLimitError('The form title is required.')).toBe(false);
  });
});
