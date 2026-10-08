import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { FieldResponse } from '../api/types';
import FieldInput from './FieldInput';

const baseField: FieldResponse = {
  id: 'f1',
  type: 'text',
  label: 'Name',
  isRequired: false,
  order: 0,
  maxLength: 500,
};

describe('FieldInput', () => {
  it('renders a text input with its label', () => {
    render(<FieldInput field={baseField} value="" onChange={() => undefined} />);

    expect(screen.getByLabelText('Name')).toHaveAttribute('type', 'text');
  });

  it('marks a required field', () => {
    render(<FieldInput field={{ ...baseField, isRequired: true }} value="" onChange={() => undefined} />);

    expect(screen.getByText('*')).toBeInTheDocument();
    expect(screen.getByLabelText(/Name/)).toBeRequired();
  });

  it('renders the options of a choice field', () => {
    const field: FieldResponse = {
      ...baseField,
      type: 'choice',
      label: 'Colour',
      options: ['red', 'blue'],
    };
    render(<FieldInput field={field} value="" onChange={() => undefined} />);

    const select = screen.getByLabelText('Colour');
    expect(select.tagName).toBe('SELECT');
    expect(screen.getByRole('option', { name: 'red' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'blue' })).toBeInTheDocument();
  });

  it('renders a number input with its bounds', () => {
    const field: FieldResponse = {
      ...baseField,
      type: 'number',
      label: 'Age',
      min: 0,
      max: 120,
    };
    render(<FieldInput field={field} value="12" onChange={() => undefined} />);

    const input = screen.getByLabelText('Age');
    expect(input).toHaveAttribute('type', 'number');
    expect(input).toHaveAttribute('min', '0');
    expect(input).toHaveAttribute('max', '120');
  });

  it('reports typing through onChange', async () => {
    const onChange = vi.fn();
    render(<FieldInput field={baseField} value="" onChange={onChange} />);

    await userEvent.type(screen.getByLabelText('Name'), 'Ada');

    expect(onChange).toHaveBeenCalled();
  });
});
